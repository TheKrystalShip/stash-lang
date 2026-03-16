"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.TapParser = void 0;
class TapParser {
    constructor(callbacks) {
        this.buffer = '';
        this.state = 0 /* State.Normal */;
        this.total = 0;
        // Pending "not ok" data while collecting YAML block
        this.pendingFailName = '';
        this.pendingFailNumber = 0;
        this.pendingYamlLines = [];
        this._awaitingYaml = false;
        this.callbacks = callbacks;
    }
    feed(chunk) {
        this.buffer += chunk;
        let newlineIdx;
        while ((newlineIdx = this.buffer.indexOf('\n')) !== -1) {
            const line = this.buffer.slice(0, newlineIdx).replace(/\r$/, '');
            this.buffer = this.buffer.slice(newlineIdx + 1);
            this.processLine(line);
        }
    }
    reset() {
        this.buffer = '';
        this.state = 0 /* State.Normal */;
        this.total = 0;
        this.pendingFailName = '';
        this.pendingFailNumber = 0;
        this.pendingYamlLines = [];
    }
    /** Process any remaining buffered content (call after the stream ends). */
    flush() {
        if (this.buffer.length > 0) {
            this.processLine(this.buffer);
            this.buffer = '';
        }
        // If a "not ok" was awaiting a YAML block, flush it now
        if (this._awaitingYaml) {
            this._awaitingYaml = false;
            this.flushPendingFail({});
        }
    }
    processLine(line) {
        if (this.state === 1 /* State.YamlBlock */) {
            this.processYamlLine(line);
            return;
        }
        // TAP version header — ignore
        if (line.startsWith('TAP version ')) {
            return;
        }
        // Plan line: 1..N
        const planMatch = /^1\.\.(\d+)$/.exec(line);
        if (planMatch) {
            const planned = parseInt(planMatch[1], 10);
            this.callbacks.onComplete?.(planned, this.total);
            return;
        }
        // ok N - name  OR  ok N - name # SKIP reason
        const passMatch = /^ok (\d+)(?: - (.*))?$/.exec(line);
        if (passMatch) {
            const testNumber = parseInt(passMatch[1], 10);
            const rawName = (passMatch[2] ?? '').trim();
            this.total++;
            // Check for SKIP directive
            const skipMatch = /^(.*?)\s*#\s*SKIP\s*(.*)$/.exec(rawName);
            if (skipMatch) {
                const name = skipMatch[1].trim();
                const reason = skipMatch[2].trim() || undefined;
                this.callbacks.onTestSkip?.(name, testNumber, reason);
            }
            else {
                this.callbacks.onTestPass?.(rawName, testNumber);
            }
            return;
        }
        // not ok N - name
        const failMatch = /^not ok (\d+)(?: - (.*))?$/.exec(line);
        if (failMatch) {
            this.pendingFailNumber = parseInt(failMatch[1], 10);
            this.pendingFailName = (failMatch[2] ?? '').trim();
            this.pendingYamlLines = [];
            this.total++;
            // YAML block may follow — wait for it or handle if no YAML
            // We'll start collecting; if next line is '  ---' we enter YAML mode,
            // otherwise flush immediately on the next non-blank line.
            this.state = 0 /* State.Normal */; // Will switch to YamlBlock when '---' is seen
            this._awaitingYaml = true;
            return;
        }
        // If we were waiting for a YAML block and got something else, flush the fail now
        if (this._awaitingYaml) {
            this._awaitingYaml = false;
            if (line.trimStart() === '---') {
                this.state = 1 /* State.YamlBlock */;
                return;
            }
            // No YAML block — emit fail with empty details then re-process this line
            this.flushPendingFail({});
            this.processLine(line);
            return;
        }
        // Comment lines
        if (line.startsWith('#')) {
            this.processComment(line);
            return;
        }
        // Blank lines and unknown lines — skip
    }
    processYamlLine(line) {
        const trimmed = line.trim();
        if (trimmed === '...') {
            // End of YAML block
            this.state = 0 /* State.Normal */;
            this._awaitingYaml = false;
            const details = this.parseYamlBlock(this.pendingYamlLines);
            this.flushPendingFail(details);
            return;
        }
        this.pendingYamlLines.push(line);
    }
    parseYamlBlock(lines) {
        const details = {};
        let inAt = false;
        for (const line of lines) {
            const trimmed = line.trim();
            if (trimmed === '') {
                continue;
            }
            // Detect nested `at:` block
            if (trimmed === 'at:') {
                inAt = true;
                continue;
            }
            const kvMatch = /^(\w+):\s*(.*)$/.exec(trimmed);
            if (!kvMatch) {
                // If we hit a non-indented key after `at:`, leave at-block
                if (!line.startsWith('    ') && !line.startsWith('\t')) {
                    inAt = false;
                }
                continue;
            }
            const key = kvMatch[1];
            const rawValue = kvMatch[2].trim();
            const value = this.unquote(rawValue);
            if (inAt) {
                switch (key) {
                    case 'file':
                        details.file = value;
                        break;
                    case 'line':
                        details.line = parseInt(value, 10) || undefined;
                        break;
                    case 'column':
                        details.column = parseInt(value, 10) || undefined;
                        break;
                }
            }
            else {
                switch (key) {
                    case 'message':
                        details.message = value;
                        break;
                    case 'expected':
                        details.expected = value;
                        break;
                    case 'actual':
                        details.actual = value;
                        break;
                    case 'at':
                        // `at:` on the same line as a value — shouldn't happen in our format
                        inAt = true;
                        break;
                }
            }
        }
        return details;
    }
    unquote(value) {
        if ((value.startsWith('"') && value.endsWith('"')) ||
            (value.startsWith("'") && value.endsWith("'"))) {
            try {
                return JSON.parse(value);
            }
            catch {
                return value.slice(1, -1);
            }
        }
        return value;
    }
    flushPendingFail(details) {
        this.callbacks.onTestFail?.(this.pendingFailName, this.pendingFailNumber, details);
        this.pendingFailName = '';
        this.pendingFailNumber = 0;
        this.pendingYamlLines = [];
    }
    processComment(line) {
        // # discovered: name [file:line:col]
        const discoveredMatch = /^# discovered: (.+?) \[([^\]]+):(\d+):(\d+)\]$/.exec(line);
        if (discoveredMatch) {
            const name = discoveredMatch[1].trim();
            const file = discoveredMatch[2];
            const discoveredLine = parseInt(discoveredMatch[3], 10);
            const column = parseInt(discoveredMatch[4], 10);
            this.callbacks.onTestDiscovered?.(name, file, discoveredLine, column);
            return;
        }
        // # suite name (any other comment is treated as a suite start)
        const suiteName = line.slice(1).trim();
        if (suiteName !== '') {
            this.callbacks.onSuiteStart?.(suiteName);
        }
    }
}
exports.TapParser = TapParser;
//# sourceMappingURL=tapParser.js.map