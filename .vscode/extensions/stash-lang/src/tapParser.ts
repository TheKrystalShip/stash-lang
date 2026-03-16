export interface TapFailureDetails {
    message?: string;
    expected?: string;
    actual?: string;
    file?: string;
    line?: number;
    column?: number;
}

export interface TapParserCallbacks {
    onTestPass?: (name: string, testNumber: number) => void;
    onTestFail?: (name: string, testNumber: number, details: TapFailureDetails) => void;
    onTestDiscovered?: (name: string, file: string, line: number, column: number) => void;
    onSuiteStart?: (name: string) => void;
    onComplete?: (planned: number, total: number) => void;
}

const enum State {
    Normal,
    YamlBlock,
}

export class TapParser {
    private readonly callbacks: TapParserCallbacks;
    private buffer: string = '';
    private state: State = State.Normal;
    private total: number = 0;

    // Pending "not ok" data while collecting YAML block
    private pendingFailName: string = '';
    private pendingFailNumber: number = 0;
    private pendingYamlLines: string[] = [];

    constructor(callbacks: TapParserCallbacks) {
        this.callbacks = callbacks;
    }

    feed(chunk: string): void {
        this.buffer += chunk;
        let newlineIdx: number;
        while ((newlineIdx = this.buffer.indexOf('\n')) !== -1) {
            const line = this.buffer.slice(0, newlineIdx).replace(/\r$/, '');
            this.buffer = this.buffer.slice(newlineIdx + 1);
            this.processLine(line);
        }
    }

    reset(): void {
        this.buffer = '';
        this.state = State.Normal;
        this.total = 0;
        this.pendingFailName = '';
        this.pendingFailNumber = 0;
        this.pendingYamlLines = [];
    }

    /** Process any remaining buffered content (call after the stream ends). */
    flush(): void {
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

    private processLine(line: string): void {
        if (this.state === State.YamlBlock) {
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

        // ok N - name
        const passMatch = /^ok (\d+)(?: - (.*))?$/.exec(line);
        if (passMatch) {
            const testNumber = parseInt(passMatch[1], 10);
            const name = (passMatch[2] ?? '').trim();
            this.total++;
            this.callbacks.onTestPass?.(name, testNumber);
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
            this.state = State.Normal; // Will switch to YamlBlock when '---' is seen
            this._awaitingYaml = true;
            return;
        }

        // If we were waiting for a YAML block and got something else, flush the fail now
        if (this._awaitingYaml) {
            this._awaitingYaml = false;
            if (line.trimStart() === '---') {
                this.state = State.YamlBlock;
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

    private _awaitingYaml: boolean = false;

    private processYamlLine(line: string): void {
        const trimmed = line.trim();
        if (trimmed === '...') {
            // End of YAML block
            this.state = State.Normal;
            this._awaitingYaml = false;
            const details = this.parseYamlBlock(this.pendingYamlLines);
            this.flushPendingFail(details);
            return;
        }
        this.pendingYamlLines.push(line);
    }

    private parseYamlBlock(lines: string[]): TapFailureDetails {
        const details: TapFailureDetails = {};
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
                    case 'file':   details.file = value; break;
                    case 'line':   details.line = parseInt(value, 10) || undefined; break;
                    case 'column': details.column = parseInt(value, 10) || undefined; break;
                }
            } else {
                switch (key) {
                    case 'message':  details.message = value; break;
                    case 'expected': details.expected = value; break;
                    case 'actual':   details.actual = value; break;
                    case 'at':
                        // `at:` on the same line as a value — shouldn't happen in our format
                        inAt = true;
                        break;
                }
            }
        }

        return details;
    }

    private unquote(value: string): string {
        if (
            (value.startsWith('"') && value.endsWith('"')) ||
            (value.startsWith("'") && value.endsWith("'"))
        ) {
            try {
                return JSON.parse(value);
            } catch {
                return value.slice(1, -1);
            }
        }
        return value;
    }

    private flushPendingFail(details: TapFailureDetails): void {
        this.callbacks.onTestFail?.(this.pendingFailName, this.pendingFailNumber, details);
        this.pendingFailName = '';
        this.pendingFailNumber = 0;
        this.pendingYamlLines = [];
    }

    private processComment(line: string): void {
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
