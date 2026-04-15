import * as vscode from "vscode";

/**
 * Tracks breakpoints created by the agent tools, keeping them separate
 * from user-set breakpoints.
 */
export class BreakpointTracker {
    /** Map from normalized file path → set of agent-managed breakpoints */
    private readonly _agentBreakpoints = new Map<string, Set<vscode.SourceBreakpoint>>();

    /**
     * Replace all agent breakpoints for a file with a new set.
     * Returns the newly created breakpoints.
     */
    replaceBreakpoints(
        fileUri: vscode.Uri,
        breakpoints: vscode.SourceBreakpoint[]
    ): vscode.SourceBreakpoint[] {
        const key = fileUri.toString();

        // Remove existing agent breakpoints for this file
        this.removeForFile(fileUri);

        // Track and add new breakpoints
        if (breakpoints.length > 0) {
            const bpSet = new Set(breakpoints);
            this._agentBreakpoints.set(key, bpSet);
            vscode.debug.addBreakpoints(breakpoints);
        }

        return breakpoints;
    }

    /**
     * Remove agent breakpoints for a specific file.
     * Returns the number of breakpoints removed.
     */
    removeForFile(fileUri: vscode.Uri): number {
        const key = fileUri.toString();
        const existing = this._agentBreakpoints.get(key);
        if (!existing || existing.size === 0) {
            return 0;
        }

        const toRemove = Array.from(existing);
        vscode.debug.removeBreakpoints(toRemove);
        this._agentBreakpoints.delete(key);
        return toRemove.length;
    }

    /**
     * Remove all agent-managed breakpoints across all files.
     * Returns the total number removed.
     */
    removeAll(): number {
        let total = 0;
        for (const [, bpSet] of this._agentBreakpoints) {
            const toRemove = Array.from(bpSet);
            vscode.debug.removeBreakpoints(toRemove);
            total += toRemove.length;
        }
        this._agentBreakpoints.clear();
        return total;
    }

    /**
     * Check if a breakpoint was created by the agent.
     */
    isAgentBreakpoint(bp: vscode.Breakpoint): boolean {
        if (!(bp instanceof vscode.SourceBreakpoint)) {
            return false;
        }
        for (const [, bpSet] of this._agentBreakpoints) {
            if (bpSet.has(bp)) {
                return true;
            }
        }
        return false;
    }

    /**
     * Get all agent breakpoints for a file.
     */
    getForFile(fileUri: vscode.Uri): vscode.SourceBreakpoint[] {
        const key = fileUri.toString();
        const existing = this._agentBreakpoints.get(key);
        return existing ? Array.from(existing) : [];
    }

    /**
     * Get total count of agent-managed breakpoints.
     */
    get totalCount(): number {
        let total = 0;
        for (const [, bpSet] of this._agentBreakpoints) {
            total += bpSet.size;
        }
        return total;
    }
}
