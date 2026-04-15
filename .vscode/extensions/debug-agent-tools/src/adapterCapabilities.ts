import * as vscode from "vscode";
import { AdapterCapabilities, ExceptionBreakpointFilter } from "./types";

const DEFAULT_CAPABILITIES: AdapterCapabilities = {
    supportsConditionalBreakpoints: false,
    supportsHitConditionalBreakpoints: false,
    supportsLogPoints: false,
    supportsEvaluateForHovers: false,
    supportsStepBack: false,
    supportsSetVariable: false,
    supportsExceptionOptions: false,
    exceptionBreakpointFilters: [],
};

/**
 * Creates a DebugAdapterTracker that captures capabilities from the
 * initialize response.
 */
export class CapabilityTracker implements vscode.DebugAdapterTracker {
    private _capabilities: AdapterCapabilities = { ...DEFAULT_CAPABILITIES };
    private _resolved = false;
    private _resolveCapabilities: ((caps: AdapterCapabilities) => void) | undefined;
    readonly capabilitiesPromise: Promise<AdapterCapabilities>;

    constructor() {
        this.capabilitiesPromise = new Promise<AdapterCapabilities>((resolve) => {
            this._resolveCapabilities = resolve;
            // Timeout after 10s — if we haven't seen the response, use defaults
            setTimeout(() => {
                if (!this._resolved) {
                    this._resolved = true;
                    resolve({ ...DEFAULT_CAPABILITIES });
                }
            }, 10000);
        });
    }

    get capabilities(): AdapterCapabilities {
        return this._capabilities;
    }

    onDidSendMessage(message: unknown): void {
        const msg = message as { type?: string; command?: string; body?: Record<string, unknown> };
        if (
            msg.type === "response" &&
            msg.command === "initialize" &&
            msg.body
        ) {
            const body = msg.body;
            this._capabilities = {
                supportsConditionalBreakpoints: body.supportsConditionalBreakpoints === true,
                supportsHitConditionalBreakpoints: body.supportsHitConditionalBreakpoints === true,
                supportsLogPoints: body.supportsLogPoints === true,
                supportsEvaluateForHovers: body.supportsEvaluateForHovers === true,
                supportsStepBack: body.supportsStepBack === true,
                supportsSetVariable: body.supportsSetVariable === true,
                supportsExceptionOptions: body.supportsExceptionOptions === true,
                exceptionBreakpointFilters: ((body.exceptionBreakpointFilters as ExceptionBreakpointFilter[]) ?? []),
            };

            if (!this._resolved) {
                this._resolved = true;
                this._resolveCapabilities?.(this._capabilities);
            }
        }
    }
}
