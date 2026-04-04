// Node.js equivalent of bench_lexer_heavy.stash
// =============================================================================
// Benchmark: Lexer / Expression Throughput
// This script is intentionally dense with identifiers, numeric literals,
// string literals, and nested expressions. The top-level declarations stress
// the parser on startup; the inner compute() function is called
// repeatedly to stress evaluation of complex expressions.
// =============================================================================

// 60 variables with varied numeric literals
let v01 = 1001;     let v02 = 1002.5;   let v03 = 2003;     let v04 = 3004.25;
let v05 = 5005;     let v06 = 6006.75;  let v07 = 7007;     let v08 = 8008.125;
let v09 = 9009;     let v10 = 1010.5;   let v11 = 1111;     let v12 = 1212.25;
let v13 = 1313;     let v14 = 1414.75;  let v15 = 1515;     let v16 = 1616.5;
let v17 = 1717;     let v18 = 1818.125; let v19 = 1919;     let v20 = 2020.25;
let v21 = 2121;     let v22 = 2222.5;   let v23 = 2323;     let v24 = 2424.75;
let v25 = 2525;     let v26 = 2626.125; let v27 = 2727;     let v28 = 2828.25;
let v29 = 2929;     let v30 = 3030.5;   let v31 = 3131;     let v32 = 3232.75;
let v33 = 3333;     let v34 = 3434.125; let v35 = 3535;     let v36 = 3636.25;
let v37 = 3737;     let v38 = 3838.5;   let v39 = 3939;     let v40 = 4040.75;
let v41 = 4141;     let v42 = 4242.125; let v43 = 4343;     let v44 = 4444.25;
let v45 = 4545;     let v46 = 4646.5;   let v47 = 4747;     let v48 = 4848.75;
let v49 = 4949;     let v50 = 5050.125; let v51 = 5151;     let v52 = 5252.25;
let v53 = 5353;     let v54 = 5454.5;   let v55 = 5555;     let v56 = 5656.75;
let v57 = 5757;     let v58 = 5858.125; let v59 = 5959;     let v60 = 6060.25;

// String literals used in expressions
let s01 = "alpha";   let s02 = "bravo";   let s03 = "charlie"; let s04 = "delta";
let s05 = "echo";    let s06 = "foxtrot"; let s07 = "golf";    let s08 = "hotel";
let s09 = "india";   let s10 = "juliet";

// Compute function: deeply nested arithmetic over many identifiers
function compute(n) {
    let r01 = v01 + v02 + v03 + v04 + v05 + v06 + v07 + v08 + v09 + v10;
    let r02 = v11 + v12 + v13 + v14 + v15 + v16 + v17 + v18 + v19 + v20;
    let r03 = v21 + v22 + v23 + v24 + v25 + v26 + v27 + v28 + v29 + v30;
    let r04 = v31 + v32 + v33 + v34 + v35 + v36 + v37 + v38 + v39 + v40;
    let r05 = v41 + v42 + v43 + v44 + v45 + v46 + v47 + v48 + v49 + v50;
    let r06 = v51 + v52 + v53 + v54 + v55 + v56 + v57 + v58 + v59 + v60;

    // Nested expression with string interpolation and concatenation
    let tag  = s01 + "-" + s02 + "-" + n;
    let tag2 = s03.toUpperCase() + "-" + s04.toLowerCase();

    let mid  = (r01 + r02 + r03) - (r04 - r05 + r06);
    let deep = mid * 2 + (r01 - r02) * (r03 - r04) / 1000;

    return deep + tag.length + tag2.length;
}

const ITERATIONS = 100000;

console.log("=== Benchmark: Lexer / Expression Throughput ===");

let start = Date.now();
let i = 0;
let acc = 0.0;
while (i < ITERATIONS) {
    acc = acc + compute(i);
    i++;
}
let elapsed = Date.now() - start;

console.log("Iterations: " + ITERATIONS);
console.log("Result (checksum): " + acc);
console.log("Time: " + elapsed + " ms");
