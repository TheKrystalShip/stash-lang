#!/usr/bin/env bash

zero_args() {
    __ret=42
}

one_arg() {
    __ret=$(( $1 + 1 ))
}

two_args() {
    __ret=$(( $1 + $2 ))
}

three_args() {
    __ret=$(( $1 + $2 + $3 ))
}

four_args() {
    __ret=$(( $1 + $2 + $3 + $4 ))
}

compute() {
    local t=$(( $1 * 3 ))
    local u=$(( t - $1 ))
    __ret=$(( u + 1 ))
}

ITERATIONS=100000

echo "=== Benchmark: Function Calls ==="

start=$(date +%s%3N)

i=0
acc=0
while (( i < ITERATIONS )); do
    zero_args
    acc=$(( acc + __ret ))

    one_arg "$i"
    acc=$(( acc + __ret ))

    two_args "$i" 2
    acc=$(( acc + __ret ))

    three_args "$i" 2 3
    acc=$(( acc + __ret ))

    four_args "$i" 1 2 3
    acc=$(( acc + __ret ))

    compute "$i"
    acc=$(( acc + __ret ))

    (( i++ ))
done

elapsed=$(( $(date +%s%3N) - start ))

echo "Iterations: $ITERATIONS"
echo "Result (checksum): $acc"
echo "Time: $elapsed ms"
