// Node.js equivalent of bench_scope_lookup.stash
// =============================================================================
// Benchmark: Scope Lookup
// Tests variable resolution across deeply nested closure chains.
// Each call to depth1() builds a 5-level scope chain and the innermost
// function reads variables from every outer level.
// =============================================================================

function depth1() {
    let a1 = 1;
    let a2 = 2;
    function depth2() {
        let b1 = 3;
        let b2 = 4;
        function depth3() {
            let c1 = 5;
            let c2 = 6;
            function depth4() {
                let d1 = 7;
                let d2 = 8;
                function depth5() {
                    // Walks 4 levels up the closure chain for each variable
                    return a1 + a2 + b1 + b2 + c1 + c2 + d1 + d2;
                }
                return depth5();
            }
            return depth4();
        }
        return depth3();
    }
    return depth2();
}

const ITERATIONS = 100000;

console.log("=== Benchmark: Scope Lookup ===");

let start = Date.now();
let i = 0;
let sum = 0;
while (i < ITERATIONS) {
    sum = sum + depth1();
    i++;
}
let elapsed = Date.now() - start;

console.log("Iterations: " + ITERATIONS);
console.log("Result (checksum): " + sum);
console.log("Time: " + elapsed + " ms");
