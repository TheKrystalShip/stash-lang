// Node.js equivalent of bench_data_transform.stash
// Builds a CSV-style dataset, parses each row, groups by department,
// computes aggregates, and serializes the result back to a key=value summary.

const ROWS   = 30000;
const PASSES = 8;

function buildRows(n) {
    const depts = ["eng", "sales", "ops", "support", "eng", "sales"];
    const names = ["Ada", "Ben", "Cyd", "Dan", "Eli", "Fay", "Gus", "Hal"];
    const out = [];
    for (let i = 0; i < n; i++) {
        const d  = depts[i % 6];
        const nm = names[i % 8];
        const salary = 30000 + ((i * 137) % 70000);
        out.push(String(i) + "," + nm + "," + d + "," + String(salary));
    }
    return out;
}

console.log("=== Benchmark: Data Transformation ===");

const rows = buildRows(ROWS);

const start = Date.now();
let total_summary_chars = 0;
let total_count = 0;

for (let p = 0; p < PASSES; p++) {
    const counts = {};
    const sums   = {};
    const maxs   = {};

    for (let i = 0; i < rows.length; i++) {
        const parts  = rows[i].split(",");
        const dept   = parts[2];
        const salary = parseInt(parts[3], 10);

        counts[dept] = (counts[dept] || 0) + 1;
        sums[dept]   = (sums[dept] || 0) + salary;

        const mx = maxs[dept] || 0;
        if (salary > mx) {
            maxs[dept] = salary;
        }

        total_count++;
    }

    const keys = Object.keys(counts);
    const parts_out = [];
    for (let k = 0; k < keys.length; k++) {
        const key = keys[k];
        const c   = counts[key] || 0;
        const s   = sums[key] || 0;
        const mx  = maxs[key] || 0;
        const avg = Math.floor(s / c);
        const line = key + "=count:" + String(c)
                         + ",sum:"   + String(s)
                         + ",avg:"   + String(avg)
                         + ",max:"   + String(mx);
        parts_out.push(line);
    }
    const summary = parts_out.join(";");
    total_summary_chars += summary.length;
}

const elapsed = Date.now() - start;

console.log("Rows: " + ROWS + ", Passes: " + PASSES);
console.log("Total rows processed: " + total_count);
console.log("Result (checksum): " + total_summary_chars);
console.log("Time: " + elapsed + " ms");
