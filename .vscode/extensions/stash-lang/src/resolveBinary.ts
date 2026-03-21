import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { execFileSync } from "child_process";

/**
 * Resolve a binary name to an absolute path.
 * On Windows, appends ".exe" when needed.
 */
export function resolveBinary(name: string): string {
  const execName = platformBinaryName(name);

  // Try system PATH via which/where
  try {
    const cmd = process.platform === "win32" ? "where.exe" : "which";
    const result = execFileSync(cmd, [execName], {
      encoding: "utf-8",
      timeout: 5000,
      stdio: ["ignore", "pipe", "ignore"],
    }).split(/\r?\n/)[0].trim();
    if (result) return result;
  } catch {
    // not found on PATH
  }

  // Fallback: check ~/.local/bin/ (Unix only)
  if (process.platform !== "win32") {
    const candidate = path.join(os.homedir(), ".local", "bin", execName);
    try {
      fs.accessSync(candidate, fs.constants.X_OK);
      return candidate;
    } catch {
      // not found or not executable
    }
  }

  return execName;
}

/**
 * Returns the platform-appropriate binary name.
 * On Windows, appends ".exe" if not already present.
 */
function platformBinaryName(name: string): string {
  if (process.platform === "win32" && !name.endsWith(".exe")) {
    return name + ".exe";
  }
  return name;
}
