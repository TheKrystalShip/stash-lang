// Node.js equivalent of bench_algorithms.stash
// =============================================================================
// Benchmark: Algorithms
// A real-world workload combining recursion, iteration, array manipulation,
// and object usage. Times each algorithm individually and reports a total.
// =============================================================================

// --- Recursive Fibonacci ---

function fibonacci(n) {
    if (n <= 0) return 0;
    if (n === 1) return 1;
    return fibonacci(n - 1) + fibonacci(n - 2);
}

// --- Bubble Sort ---

function bubble_sort(arr) {
    let n = arr.length;
    let swapped = true;
    while (swapped) {
        swapped = false;
        let j = 0;
        while (j < n - 1) {
            if (arr[j] > arr[j + 1]) {
                let tmp = arr[j];
                arr[j] = arr[j + 1];
                arr[j + 1] = tmp;
                swapped = true;
            }
            j++;
        }
        n--;
    }
    return arr;
}

// --- Binary Search (requires sorted array) ---

function binary_search(arr, target) {
    let lo = 0;
    let hi = arr.length - 1;
    while (lo <= hi) {
        let mid = Math.floor((lo + hi) / 2);
        if (arr[mid] === target) return mid;
        if (arr[mid] < target) {
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }
    return -1;
}

// --- Object usage ---

function build_nodes(count) {
    let nodes = [];
    let i = 0;
    while (i < count) {
        nodes.push({ value: i * 3 + 1, index: i });
        i++;
    }
    return nodes;
}

function sum_nodes(nodes) {
    let total = 0;
    let i = 0;
    while (i < nodes.length) {
        total = total + nodes[i].value;
        i++;
    }
    return total;
}

// =============================================================================
// Run benchmarks
// =============================================================================

console.log("=== Benchmark: Algorithms ===");
console.log("");

// 1. Fibonacci(26) — deep recursion tree

let t0 = Date.now();

let fib_result = fibonacci(26);

let fib_time = Date.now() - t0;
console.log("fibonacci(26)      = " + fib_result);
console.log("Time: " + fib_time + " ms");
console.log("");

// 2. Bubble sort on 1000-element descending array (worst case)

let sort_data = [];
let si = 1000;
while (si > 0) {
    sort_data.push(si);
    si--;
}

let t1 = Date.now();

let sorted = bubble_sort(sort_data);

let sort_time = Date.now() - t1;
console.log("bubble_sort(1000)  first=" + sorted[0] + ", last=" + sorted[sorted.length - 1]);
console.log("Time: " + sort_time + " ms");
console.log("");

// 3. Binary search — 10,000 searches on the sorted array

let t2 = Date.now();

let found_count = 0;
let bi = 0;
while (bi < 10000) {
    let target = (bi % 1000) + 1;
    let idx = binary_search(sorted, target);
    if (idx >= 0) {
        found_count++;
    }
    bi++;
}

let search_time = Date.now() - t2;
console.log("binary_search x10000  found=" + found_count);
console.log("Time: " + search_time + " ms");
console.log("");

// 4. Object build + aggregate — 5000 Node instances

let t3 = Date.now();

let nodes = build_nodes(5000);
let node_sum = sum_nodes(nodes);

let struct_time = Date.now() - t3;
console.log("build+sum nodes(5000) sum=" + node_sum);
console.log("Time: " + struct_time + " ms");
console.log("");

let total_time = fib_time + sort_time + search_time + struct_time;
console.log("Total time: " + total_time + " ms");
