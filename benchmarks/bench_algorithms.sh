#!/usr/bin/env bash

# =============================================================================
# Benchmark: Algorithms (Bash equivalent of bench_algorithms.stash)
# Workload: recursive fibonacci, bubble sort, binary search, struct simulation
# =============================================================================

__ret=0

# --- Recursive Fibonacci ---
# Returns result in global __ret to avoid slow echo-subshell capture.

fibonacci() {
    local n=$1
    if (( n <= 0 )); then __ret=0; return; fi
    if (( n == 1 )); then __ret=1; return; fi
    fibonacci $(( n - 1 ))
    local r1=$__ret
    fibonacci $(( n - 2 ))
    __ret=$(( r1 + __ret ))
}

# --- Bubble Sort ---
# Operates on global sort_arr in-place (worst-case descending input).

declare -a sort_arr

bubble_sort() {
    local n=${#sort_arr[@]}
    local swapped=1
    local j tmp
    while (( swapped )); do
        swapped=0
        j=0
        while (( j < n - 1 )); do
            if (( sort_arr[j] > sort_arr[j+1] )); then
                tmp=${sort_arr[j]}
                sort_arr[j]=${sort_arr[j+1]}
                sort_arr[j+1]=$tmp
                swapped=1
            fi
            j=$(( j + 1 ))
        done
        n=$(( n - 1 ))
    done
}

# --- Binary Search ---
# Searches global sort_arr for target; stores found index in __ret (-1 = miss).

binary_search() {
    local target=$1
    local lo=0
    local hi=$(( ${#sort_arr[@]} - 1 ))
    local mid
    while (( lo <= hi )); do
        mid=$(( (lo + hi) / 2 ))
        if (( sort_arr[mid] == target )); then __ret=$mid; return; fi
        if (( sort_arr[mid] < target )); then
            lo=$(( mid + 1 ))
        else
            hi=$(( mid - 1 ))
        fi
    done
    __ret=-1
}

# --- Node struct simulation via parallel arrays ---
# Mirrors: struct Node { value, index }  with  value = i*3+1, index = i

declare -a node_values node_indices

build_nodes() {
    local count=$1
    local i=0
    while (( i < count )); do
        node_values[i]=$(( i * 3 + 1 ))
        node_indices[i]=$i
        i=$(( i + 1 ))
    done
}

sum_nodes() {
    local total=0
    local i=0
    local len=${#node_values[@]}
    while (( i < len )); do
        total=$(( total + node_values[i] ))
        i=$(( i + 1 ))
    done
    __ret=$total
}

# =============================================================================
# Run benchmarks
# =============================================================================

echo "=== Benchmark: Algorithms ==="
echo ""

# 1. Fibonacci(26) — deep recursion tree

t0=$(date +%s%3N)

fibonacci 26
fib_result=$__ret

fib_time=$(( $(date +%s%3N) - t0 ))
echo "fibonacci(26)      = $fib_result"
echo "Time: $fib_time ms"
echo ""

# 2. Bubble sort on 1000 integers (descending = worst case)

si=1000
sort_arr=()
while (( si > 0 )); do
    sort_arr+=( $si )
    si=$(( si - 1 ))
done

t1=$(date +%s%3N)

bubble_sort

sort_time=$(( $(date +%s%3N) - t1 ))
last_idx=$(( ${#sort_arr[@]} - 1 ))
echo "bubble_sort(1000)  first=${sort_arr[0]}, last=${sort_arr[$last_idx]}"
echo "Time: $sort_time ms"
echo ""

# 3. Binary search — 10,000 searches on the sorted array

t2=$(date +%s%3N)

found_count=0
bi=0
while (( bi < 10000 )); do
    target=$(( bi % 1000 + 1 ))
    binary_search $target
    if (( __ret >= 0 )); then
        found_count=$(( found_count + 1 ))
    fi
    bi=$(( bi + 1 ))
done

search_time=$(( $(date +%s%3N) - t2 ))
echo "binary_search x10000  found=$found_count"
echo "Time: $search_time ms"
echo ""

# 4. Node struct simulation — 5000 nodes, sum all values

t3=$(date +%s%3N)

build_nodes 5000
sum_nodes
node_sum=$__ret

struct_time=$(( $(date +%s%3N) - t3 ))
echo "build+sum nodes(5000) sum=$node_sum"
echo "Time: $struct_time ms"
echo ""

total_time=$(( fib_time + sort_time + search_time + struct_time ))
echo "Total time: $total_time ms"
