// Node.js equivalent of bench_function_calls.stash
// =============================================================================
// Benchmark: Function Call Overhead
// Tests the cost of function dispatch, argument passing, and environment
// setup across functions of varying arities.
// =============================================================================

function zero_args() {
    return 42;
}

function one_arg(x) {
    return x + 1;
}

function two_args(a, b) {
    return a + b;
}

function three_args(a, b, c) {
    return a + b + c;
}

function four_args(a, b, c, d) {
    return a + b + c + d;
}

// A slightly heavier function to mix in

function compute(x) {
    let t = x * 3;
    let u = t - x;
    return u + 1;
}

const ITERATIONS = 100000;
console.log("=== Benchmark: Function Calls ===");

let start = Date.now();

let i = 0;
let acc = 0;

while (i < ITERATIONS) {
    acc = acc + zero_args();
    acc = acc + one_arg(i);
    acc = acc + two_args(i, 2);
    acc = acc + three_args(i, 2, 3);
    acc = acc + four_args(i, 1, 2, 3);
    acc = acc + compute(i);
    i++;
}

let elapsed = Date.now() - start;
console.log("Iterations: " + ITERATIONS);
console.log("Result (checksum): " + acc);
console.log("Time: " + elapsed + " ms");
