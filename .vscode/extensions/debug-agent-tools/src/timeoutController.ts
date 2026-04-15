import * as vscode from "vscode";

/**
 * Wraps a promise with a timeout. If the promise doesn't resolve within
 * the given milliseconds, resolves with the timeoutValue.
 */
export function withTimeout<T>(
    promise: Promise<T>,
    timeoutMs: number,
    timeoutValue: T
): Promise<T> {
    return new Promise<T>((resolve) => {
        const timer = setTimeout(() => resolve(timeoutValue), timeoutMs);
        promise.then(
            (value) => {
                clearTimeout(timer);
                resolve(value);
            },
            () => {
                clearTimeout(timer);
                resolve(timeoutValue);
            }
        );
    });
}

/**
 * Waits for a VS Code event to fire, with a timeout.
 * Returns the event payload or undefined if timed out.
 */
export function waitForEvent<T>(
    event: vscode.Event<T>,
    predicate: (value: T) => boolean,
    timeoutMs: number
): Promise<T | undefined> {
    return new Promise<T | undefined>((resolve) => {
        const timer = setTimeout(() => {
            disposable.dispose();
            resolve(undefined);
        }, timeoutMs);

        const disposable = event((value) => {
            if (predicate(value)) {
                clearTimeout(timer);
                disposable.dispose();
                resolve(value);
            }
        });
    });
}
