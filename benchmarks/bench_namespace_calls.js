// Node.js equivalent of bench_namespace_calls.stash
// =============================================================================
// Benchmark: Namespace Member Lookup
// Tests the cost of resolving built-in functions across different namespaces
// (math.*, str.*, conv.*). Each iteration performs multiple lookups to stress
// the dispatch path.
// =============================================================================

const ITERATIONS = 200000;

console.log("=== Benchmark: Namespace Calls ===");

let start = Date.now();
let i = 0;
let acc = 0.0;
while (i < ITERATIONS) {
    // math namespace
    let sq  = Math.sqrt(i + 1);
    let ab  = Math.abs(-i - 1);
    let fl  = Math.floor(sq + 0.5);
    let pw  = Math.pow(2, 8);
    let mx  = Math.max(i, i + 1);
    let mn  = Math.min(i, i + 1);

    // str namespace
    let up  = "hello".toUpperCase();
    let lo  = "WORLD".toLowerCase();
    let tr  = "  stash  ".trim();
    let rp  = "abc".replace("b", "B");

    // conv namespace
    let cs  = String(i);
    let ci  = parseInt("42");
    let chx = (255).toString(16);

    acc = acc + sq + ab + fl + pw + mx + mn;
    i++;
}
let elapsed = Date.now() - start;

console.log("Iterations: " + ITERATIONS);
console.log("Result (checksum): " + acc);
console.log("Time: " + elapsed + " ms");
