"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.activateTesting = activateTesting;
const vscode = __importStar(require("vscode"));
const path = __importStar(require("path"));
const child_process_1 = require("child_process");
const tapParser_1 = require("./tapParser");
const testDiscovery_1 = require("./testDiscovery");
const codeLensProvider_1 = require("./codeLensProvider");
// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
function getInterpreterPath() {
    const config = vscode.workspace.getConfiguration('stash');
    return config.get('interpreterPath', '') || 'stash';
}
function getFilePattern() {
    const config = vscode.workspace.getConfiguration('stash.testing');
    return config.get('filePattern', '**/*.test.stash');
}
// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------
/** Maps fully qualified test name → TestItem for O(1) TAP lookup */
const itemMap = new Map();
// Debounce timers keyed by file URI string
const debounceTimers = new Map();
const discoveryVersions = new Map();
// ---------------------------------------------------------------------------
// Activation
// ---------------------------------------------------------------------------
function activateTesting(context) {
    const controller = vscode.tests.createTestController('stashTests', 'Stash - Test Explorer');
    context.subscriptions.push(controller);
    // -- Run profile ----------------------------------------------------------
    const runProfile = controller.createRunProfile('Stash: Run Tests', vscode.TestRunProfileKind.Run, (request, token) => runHandler(controller, request, token), 
    /* isDefault */ true);
    context.subscriptions.push(runProfile);
    // -- Debug profile --------------------------------------------------------
    const debugProfile = controller.createRunProfile('Stash: Debug Tests', vscode.TestRunProfileKind.Debug, (request, token) => debugHandler(controller, request, token), 
    /* isDefault */ false);
    context.subscriptions.push(debugProfile);
    // -- CodeLens provider ----------------------------------------------------
    const codeLensProvider = new codeLensProvider_1.StashTestCodeLensProvider();
    context.subscriptions.push(vscode.languages.registerCodeLensProvider({ language: 'stash', pattern: '**/*.test.stash' }, codeLensProvider));
    // -- CodeLens commands ----------------------------------------------------
    context.subscriptions.push(vscode.commands.registerCommand('stash.runTestByName', async (filePath, name, line) => {
        const item = await resolveTestItem(controller, filePath, name, line);
        if (!item) {
            return;
        }
        const request = new vscode.TestRunRequest([item], undefined, runProfile);
        await runHandler(controller, request, new vscode.CancellationTokenSource().token);
    }), vscode.commands.registerCommand('stash.debugTestByName', async (filePath, name, line) => {
        const item = await resolveTestItem(controller, filePath, name, line);
        if (!item) {
            return;
        }
        const request = new vscode.TestRunRequest([item], undefined, debugProfile);
        await debugHandler(controller, request, new vscode.CancellationTokenSource().token);
    }));
    // -- File watcher ---------------------------------------------------------
    const pattern = getFilePattern();
    for (const folder of vscode.workspace.workspaceFolders ?? []) {
        const relPattern = new vscode.RelativePattern(folder, pattern);
        const watcher = vscode.workspace.createFileSystemWatcher(relPattern);
        watcher.onDidCreate(uri => discoverTestsInFile(controller, uri));
        watcher.onDidChange(uri => discoverTestsInFile(controller, uri));
        watcher.onDidDelete(uri => {
            const existing = controller.items.get('file:' + uri.fsPath);
            if (existing) {
                controller.items.delete(existing.id);
            }
            // Clean up itemMap entries belonging to this file
            const prefix = uri.fsPath + ' > ';
            for (const key of itemMap.keys()) {
                if (key === uri.fsPath || key.startsWith(prefix)) {
                    itemMap.delete(key);
                }
            }
        });
        context.subscriptions.push(watcher);
        // Initial scan
        vscode.workspace.findFiles(relPattern).then(uris => {
            for (const uri of uris) {
                discoverTestsInFile(controller, uri);
            }
        });
    }
}
// ---------------------------------------------------------------------------
// Discovery
// ---------------------------------------------------------------------------
async function discoverTestsInFile(controller, uri) {
    const autoDiscover = vscode.workspace.getConfiguration('stash.testing').get('autoDiscover', true);
    if (!autoDiscover) {
        return;
    }
    let text;
    try {
        text = await vscode.workspace.fs.readFile(uri);
    }
    catch {
        return;
    }
    const content = Buffer.from(text).toString('utf8');
    const fileName = path.basename(uri.fsPath);
    const staticTests = (0, testDiscovery_1.parseTestsFromText)(content, fileName);
    buildTestItems(controller, uri, staticTests);
    // Kick off dynamic discovery (debounced at 500 ms)
    const key = uri.toString();
    const existing = debounceTimers.get(key);
    if (existing !== undefined) {
        clearTimeout(existing);
    }
    const version = (discoveryVersions.get(key) ?? 0) + 1;
    discoveryVersions.set(key, version);
    const timer = setTimeout(async () => {
        debounceTimers.delete(key);
        const interpreterPath = getInterpreterPath();
        try {
            const dynamicTests = await (0, testDiscovery_1.discoverTestsDynamic)(uri.fsPath, interpreterPath);
            // Only apply if this is still the latest discovery request
            if (discoveryVersions.get(key) === version && dynamicTests.length > 0) {
                buildTestItems(controller, uri, dynamicTests);
            }
        }
        catch {
            // Interpreter not available — static discovery is sufficient
        }
    }, 500);
    debounceTimers.set(key, timer);
}
function buildTestItems(controller, uri, tests) {
    if (tests.length === 0) {
        return;
    }
    const fileId = 'file:' + uri.fsPath;
    const fileName = path.basename(uri.fsPath);
    // Get or create file-level item
    let fileItem = controller.items.get(fileId);
    if (!fileItem) {
        fileItem = controller.createTestItem(fileId, fileName, uri);
        controller.items.add(fileItem);
    }
    fileItem.canResolveChildren = true;
    // Track which child IDs are still present so we can prune stale ones
    const seenIds = new Set();
    for (const test of tests) {
        // Walk ancestors (skip the first entry which is the file name)
        let parent = fileItem;
        const ancestorNames = test.ancestors.slice(1); // remove filename
        for (let i = 0; i < ancestorNames.length; i++) {
            const ancestorName = ancestorNames[i];
            // ancestors[0] is the filename; ancestors[1..] are describe names
            // ancestorNames = ancestors.slice(1), so the full path is ancestors[0..i+2)
            const pathSegments = test.ancestors.slice(1, i + 2);
            const ancestorId = buildId(uri.fsPath, pathSegments);
            seenIds.add(ancestorId);
            let ancestorItem = parent.children.get(ancestorId);
            if (!ancestorItem) {
                ancestorItem = controller.createTestItem(ancestorId, ancestorName, uri);
                parent.children.add(ancestorItem);
                itemMap.set(ancestorId, ancestorItem);
            }
            parent = ancestorItem;
        }
        // Build fully qualified ID for the leaf test
        const testId = buildId(uri.fsPath, [...test.ancestors.slice(1), test.label]);
        seenIds.add(testId);
        let testItem = parent.children.get(testId);
        if (!testItem) {
            testItem = controller.createTestItem(testId, test.label, uri);
            parent.children.add(testItem);
        }
        // Set range for gutter icon and click-to-navigate
        testItem.range = new vscode.Range(new vscode.Position(test.line, test.column), new vscode.Position(test.line, test.column + test.label.length));
        itemMap.set(testId, testItem);
        // Also register the TAP fully qualified name (uses ' > ' separator)
        itemMap.set(test.fullName, testItem);
    }
    // Prune stale children from fileItem that are no longer discovered
    pruneChildren(fileItem, seenIds);
}
/**
 * Build a stable test item ID from the absolute file path and descriptive
 * path segments (describe names + test name).
 */
function buildId(filePath, segments) {
    return [filePath, ...segments].join(' > ');
}
function pruneChildren(parent, keep) {
    const toDelete = [];
    parent.children.forEach(child => {
        if (!keep.has(child.id)) {
            toDelete.push(child.id);
            itemMap.delete(child.id);
        }
        else {
            pruneChildren(child, keep);
        }
    });
    for (const id of toDelete) {
        parent.children.delete(id);
    }
}
// ---------------------------------------------------------------------------
// Run handler
// ---------------------------------------------------------------------------
async function runHandler(controller, request, token) {
    const run = controller.createTestRun(request);
    // Collect the test items to run
    const included = collectItems(request.include ?? allItems(controller));
    // Mark all as enqueued
    for (const item of included) {
        run.enqueued(item);
    }
    // Group by file
    const fileGroups = groupByFile(included);
    if (fileGroups.size === 0) {
        run.end();
        return;
    }
    // Run files in parallel
    const promises = Array.from(fileGroups.entries()).map(([filePath, tests]) => runTestFile(filePath, tests, run, token));
    try {
        await Promise.all(promises);
    }
    finally {
        run.end();
    }
}
function runTestFile(filePath, tests, run, token) {
    return new Promise(resolve => {
        const interpreterPath = getInterpreterPath();
        // Build filter: if we're running specific (non-file-level) tests,
        // pass their labels joined by semicolons
        const filter = buildFilter(tests);
        const args = ['--test'];
        if (filter) {
            args.push(`--test-filter=${filter}`);
        }
        args.push(filePath);
        let proc;
        try {
            proc = (0, child_process_1.spawn)(interpreterPath, args, {
                stdio: ['ignore', 'pipe', 'pipe'],
            });
        }
        catch {
            vscode.window.showErrorMessage(`Stash interpreter not found: "${interpreterPath}". ` +
                `Check the stash.interpreterPath setting.`);
            for (const t of tests) {
                run.skipped(t);
            }
            resolve();
            return;
        }
        const startTime = Date.now();
        const parser = new tapParser_1.TapParser({
            onTestPass(name, _testNumber) {
                const item = findItem(name, tests);
                if (item) {
                    run.passed(item, Date.now() - startTime);
                }
            },
            onTestFail(name, _testNumber, details) {
                const item = findItem(name, tests);
                if (item) {
                    const msg = buildTestMessage(item, details);
                    run.failed(item, msg, Date.now() - startTime);
                }
            },
            onTestSkip(name, _testNumber, _reason) {
                const item = findItem(name, tests);
                if (item) {
                    run.skipped(item);
                }
            },
        });
        token.onCancellationRequested(() => {
            proc.kill('SIGTERM');
        });
        proc.stdout?.on('data', (chunk) => {
            parser.feed(chunk.toString());
        });
        proc.on('error', (err) => {
            if (!token.isCancellationRequested) {
                vscode.window.showErrorMessage(`Failed to run Stash tests: ${err.message}`);
            }
            for (const t of tests) {
                run.skipped(t);
            }
            resolve();
        });
        proc.on('close', () => {
            parser.flush();
            resolve();
        });
    });
}
function buildFilter(tests) {
    // If any item is a file-level item, run everything (no filter needed)
    if (tests.some(t => t.id.startsWith('file:'))) {
        return '';
    }
    // Convert item IDs to TAP fully qualified names
    // ID format: "/full/path/file.test.stash > describe > test"
    // TAP format: "file.test.stash > describe > test"
    return tests.map(t => itemIdToTapName(t.id)).join(';');
}
function itemIdToTapName(id) {
    const firstSep = id.indexOf(' > ');
    if (firstSep === -1) {
        return id;
    }
    const filePath = id.slice(0, firstSep);
    const rest = id.slice(firstSep); // " > describe > test"
    return path.basename(filePath) + rest;
}
function findItem(tapName, candidates) {
    // Try exact match in itemMap first
    const fromMap = itemMap.get(tapName);
    if (fromMap) {
        return fromMap;
    }
    // Fallback: find by label among candidates
    return candidates.find(t => t.label === tapName || t.id.endsWith(' > ' + tapName));
}
function buildTestMessage(item, details) {
    const msg = new vscode.TestMessage(details.message ?? 'Test failed');
    if (details.expected !== undefined) {
        msg.expectedOutput = details.expected;
    }
    if (details.actual !== undefined) {
        msg.actualOutput = details.actual;
    }
    if (item.uri && details.line !== undefined) {
        const line = Math.max(0, details.line - 1); // convert 1-based → 0-based
        const col = Math.max(0, (details.column ?? 1) - 1);
        msg.location = new vscode.Location(item.uri, new vscode.Position(line, col));
    }
    return msg;
}
// ---------------------------------------------------------------------------
// Debug handler
// ---------------------------------------------------------------------------
async function debugHandler(controller, request, token) {
    const run = controller.createTestRun(request);
    const included = collectItems(request.include ?? allItems(controller));
    if (included.length === 0) {
        run.end();
        return;
    }
    // Mark enqueued
    for (const item of included) {
        run.enqueued(item);
    }
    // Pick first file to debug (debug one file at a time)
    const fileGroups = groupByFile(included);
    const [[filePath, tests]] = Array.from(fileGroups.entries());
    const workspaceFolder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(filePath));
    if (!workspaceFolder) {
        vscode.window.showErrorMessage('Could not determine workspace folder for debug session.');
        run.end();
        return;
    }
    const filter = buildFilter(tests);
    const firstTest = tests[0];
    // Set up a TAP parser to handle debug output
    const tapParser = new tapParser_1.TapParser({
        onTestPass(name, _testNumber) {
            const item = findItem(name, tests);
            if (item) {
                run.passed(item);
            }
        },
        onTestFail(name, _testNumber, details) {
            const item = findItem(name, tests);
            if (item) {
                const msg = buildTestMessage(item, details);
                run.failed(item, msg);
            }
        },
        onTestSkip(name, _testNumber, _reason) {
            const item = findItem(name, tests);
            if (item) {
                run.skipped(item);
            }
        },
    });
    // Register a tracker factory to intercept DAP stdout output events
    const trackerDisposable = vscode.debug.registerDebugAdapterTrackerFactory('stash', {
        createDebugAdapterTracker(_session) {
            return {
                onDidSendMessage(msg) {
                    if (msg.type === 'event' &&
                        msg.event === 'output' &&
                        msg.body?.category === 'stdout' &&
                        msg.body.output) {
                        tapParser.feed(msg.body.output);
                    }
                },
            };
        },
    });
    const debugConfig = {
        type: 'stash',
        request: 'launch',
        name: `Debug: ${firstTest.label}`,
        program: filePath,
        stopOnEntry: false,
        cwd: workspaceFolder.uri.fsPath,
        __testMode: true,
        ...(filter ? { __testFilter: filter } : {}),
    };
    let sessionStarted = false;
    try {
        sessionStarted = await vscode.debug.startDebugging(workspaceFolder, debugConfig);
    }
    catch (err) {
        vscode.window.showErrorMessage(`Failed to start debug session: ${err}`);
    }
    if (!sessionStarted) {
        trackerDisposable.dispose();
        run.end();
        return;
    }
    // Wait for debug session to end
    const sessionEndDisposable = vscode.debug.onDidTerminateDebugSession((endedSession) => {
        // Only handle our debug session — check by name and config
        if (endedSession.name === debugConfig.name) {
            sessionEndDisposable.dispose();
            trackerDisposable.dispose();
            tapParser.flush();
            run.end();
        }
    });
    token.onCancellationRequested(() => {
        vscode.debug.stopDebugging();
    });
}
// ---------------------------------------------------------------------------
// Utilities
// ---------------------------------------------------------------------------
/**
 * Look up a TestItem by file path, label, and line number.
 * If not found, trigger discovery and retry once.
 */
async function resolveTestItem(controller, filePath, name, line) {
    let item = findItemByPosition(filePath, name, line);
    if (!item) {
        // Discovery may not have run yet — trigger it and retry
        await discoverTestsInFile(controller, vscode.Uri.file(filePath));
        item = findItemByPosition(filePath, name, line);
    }
    if (!item) {
        vscode.window.showWarningMessage(`Could not find test "${name}". Try saving the file first.`);
    }
    return item;
}
function findItemByPosition(filePath, name, line) {
    let fallback;
    for (const item of itemMap.values()) {
        if (item.label === name && item.uri?.fsPath === filePath) {
            if (item.range?.start.line === line) {
                return item;
            }
            fallback ?? (fallback = item);
        }
    }
    return fallback;
}
/** Flatten a TestItemCollection into a plain array. */
function allItems(controller) {
    const result = [];
    controller.items.forEach(item => result.push(item));
    return result;
}
/** Recursively collect leaf test items (or file-level items when appropriate). */
function collectItems(items) {
    const result = [];
    for (const item of items) {
        if (item.children.size === 0) {
            result.push(item);
        }
        else {
            // Include the item itself so the file-level filter works
            result.push(item);
        }
    }
    return result;
}
/**
 * Group test items by the absolute file path they belong to.
 * File-level items (id starts with "file:") map to their path.
 * Leaf items use their URI.
 */
function groupByFile(items) {
    const groups = new Map();
    for (const item of items) {
        let filePath;
        if (item.id.startsWith('file:')) {
            filePath = item.id.slice('file:'.length);
        }
        else if (item.uri) {
            filePath = item.uri.fsPath;
        }
        if (!filePath) {
            continue;
        }
        const group = groups.get(filePath);
        if (group) {
            group.push(item);
        }
        else {
            groups.set(filePath, [item]);
        }
    }
    return groups;
}
//# sourceMappingURL=testing.js.map