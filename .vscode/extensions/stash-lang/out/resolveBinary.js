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
exports.resolveBinary = resolveBinary;
const fs = __importStar(require("fs"));
const os = __importStar(require("os"));
const path = __importStar(require("path"));
const child_process_1 = require("child_process");
/**
 * Resolve a binary name to an absolute path.
 * On Windows, appends ".exe" when needed.
 */
function resolveBinary(name) {
    const execName = platformBinaryName(name);
    // Try system PATH via which/where
    try {
        const cmd = process.platform === "win32" ? "where.exe" : "which";
        const result = (0, child_process_1.execFileSync)(cmd, [execName], {
            encoding: "utf-8",
            timeout: 5000,
            stdio: ["ignore", "pipe", "ignore"],
        }).split(/\r?\n/)[0].trim();
        if (result)
            return result;
    }
    catch {
        // not found on PATH
    }
    // Fallback: check ~/.local/bin/ (Unix only)
    if (process.platform !== "win32") {
        const candidate = path.join(os.homedir(), ".local", "bin", execName);
        try {
            fs.accessSync(candidate, fs.constants.X_OK);
            return candidate;
        }
        catch {
            // not found or not executable
        }
    }
    return execName;
}
/**
 * Returns the platform-appropriate binary name.
 * On Windows, appends ".exe" if not already present.
 */
function platformBinaryName(name) {
    if (process.platform === "win32" && !name.endsWith(".exe")) {
        return name + ".exe";
    }
    return name;
}
//# sourceMappingURL=resolveBinary.js.map