// Node.js equivalent of bench_text_processing.stash
// Generates synthetic log lines in memory, then filters/parses/aggregates them.

const LINES = 50000;
const SCANS = 20;

function buildLog(n) {
    const levels = ["INFO", "WARN", "ERROR", "DEBUG", "INFO", "ERROR"];
    const mods   = ["auth", "db", "net", "cache", "api", "fs"];
    const users  = ["alice", "bob", "carol", "dave", "eve"];
    const out = [];
    for (let i = 0; i < n; i++) {
        const lvl = levels[i % 6];
        const mn  = mods[i % 6];
        const u   = users[i % 5];
        const ts  = "2026-01-15 12:" + String(Math.floor(i / 60) % 60) + ":" + String(i % 60);
        out.push(ts + " " + lvl + " [" + mn + "] event=" + String(i) + " user=" + u);
    }
    return out;
}

console.log("=== Benchmark: Text Processing ===");

const lines = buildLog(LINES);

const start = Date.now();
let total_errors = 0;
let total_chars  = 0;

for (let s = 0; s < SCANS; s++) {
    const by_module = {};
    const by_user   = {};
    let errors = 0;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        if (line.indexOf("ERROR") !== -1) {
            errors++;
            total_chars += line.length;

            const lb = line.indexOf("[");
            const rb = line.indexOf("]");
            const modname = line.substring(lb + 1, rb);

            const up   = line.indexOf("user=");
            const user = line.substring(up + 5, line.length);

            by_module[modname] = (by_module[modname] || 0) + 1;
            by_user[user]      = (by_user[user] || 0) + 1;
        }
    }

    total_errors += errors;

    const keys = Object.keys(by_module);
    for (let k = 0; k < keys.length; k++) {
        const m = keys[k];
        const n  = by_module[m] || 0;
        const up = m.toUpperCase();
        const rp = up.replace(/_/g, "-");
        total_chars += rp.length + n;
    }
}

const elapsed = Date.now() - start;

console.log("Lines: " + LINES + ", Scans: " + SCANS);
console.log("Total errors matched: " + total_errors);
console.log("Result (checksum): " + total_chars);
console.log("Time: " + elapsed + " ms");
