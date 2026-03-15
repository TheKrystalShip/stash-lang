#!/usr/bin/env bash

depth5() {
    __ret=$(( a1 + a2 + b1 + b2 + c1 + c2 + d1 + d2 ))
}

depth4() {
    local d1=7
    local d2=8
    depth5
}

depth3() {
    local c1=5
    local c2=6
    depth4
}

depth2() {
    local b1=3
    local b2=4
    depth3
}

depth1() {
    local a1=1
    local a2=2
    depth2
}

ITERATIONS=100000

echo "=== Benchmark: Scope Lookup ==="

start=$(date +%s%3N)

i=0
sum=0
while (( i < ITERATIONS )); do
    depth1
    sum=$(( sum + __ret ))
    (( i++ ))
done

elapsed=$(( $(date +%s%3N) - start ))

echo "Iterations: $ITERATIONS"
echo "Result (checksum): $sum"
echo "Time: $elapsed ms"
