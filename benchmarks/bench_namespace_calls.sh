#!/usr/bin/env bash
# =============================================================================
# Benchmark: Namespace Calls (Bash equivalent)
# Mirrors bench_namespace_calls.stash. Tests the overhead of dispatching to
# "namespace" functions. Each bash function simulates one Stash built-in:
#   math.sqrt  → math_sqrt()    math.abs   → math_abs()
#   math.floor → math_floor()   math.pow   → math_pow()
#   math.max   → math_max()     math.min   → math_min()
#   str.upper  → str_upper()    str.lower  → str_lower()
#   str.trim   → str_trim()     str.replace → str_replace()
#   conv.toStr → conv_toStr()   conv.toInt → conv_toInt()
#   conv.toHex → conv_toHex()
#
# All functions return their result via the global __ret variable to avoid
# subshell overhead. Integer arithmetic is used throughout; math.sqrt uses
# Newton's method, math.floor is identity for integers.
# =============================================================================

# Global return value (avoids subshell overhead for function returns)
__ret=0

# ---------------------------------------------------------------------------
# math namespace
# ---------------------------------------------------------------------------

math_sqrt() {
    local n=$1
    if (( n <= 1 )); then __ret=$n; return; fi
    local x=$(( n / 2 ))
    local y=$(( (x + n / x) / 2 ))
    while (( y < x )); do
        x=$y
        y=$(( (x + n / x) / 2 ))
    done
    __ret=$x
}

math_abs() {
    local val=$1
    __ret=$(( val < 0 ? -val : val ))
}

math_floor() {
    # integers are already whole — identity function
    __ret=$1
}

math_pow() {
    # bash supports ** for integer exponentiation
    __ret=$(( $1 ** $2 ))
}

math_max() {
    __ret=$(( $1 > $2 ? $1 : $2 ))
}

math_min() {
    __ret=$(( $1 < $2 ? $1 : $2 ))
}

# ---------------------------------------------------------------------------
# str namespace
# ---------------------------------------------------------------------------

str_upper() {
    __ret="${1^^}"
}

str_lower() {
    __ret="${1,,}"
}

str_trim() {
    local str="$1"
    # strip leading whitespace
    str="${str#"${str%%[![:space:]]*}"}"
    # strip trailing whitespace
    str="${str%"${str##*[![:space:]]}"}"
    __ret="$str"
}

str_replace() {
    # str_replace <string> <search> <replacement>
    __ret="${1/"$2"/"$3"}"
}

# ---------------------------------------------------------------------------
# conv namespace
# ---------------------------------------------------------------------------

conv_toStr() {
    # everything is already a string in bash
    __ret="$1"
}

conv_toInt() {
    __ret=$(( $1 ))
}

conv_toHex() {
    printf -v __ret '%x' "$1"
}

# ---------------------------------------------------------------------------
# Main benchmark
# ---------------------------------------------------------------------------

ITERATIONS=200000

echo "=== Benchmark: Namespace Calls ==="

start=$(date +%s%3N)

i=0
acc=0
while (( i < ITERATIONS )); do
    math_sqrt $(( i + 1 ));  sq=$__ret
    math_abs  $(( -i - 1 )); ab=$__ret
    math_floor "$sq";         fl=$__ret
    math_pow  2 8;            pw=$__ret
    math_max  "$i" $(( i + 1 )); mx=$__ret
    math_min  "$i" $(( i + 1 )); mn=$__ret

    str_upper  "hello"
    str_lower  "WORLD"
    str_trim   "  stash  "
    str_replace "abc" "b" "B"

    conv_toStr "$i"
    conv_toInt "42"
    conv_toHex 255

    acc=$(( acc + sq + ab + fl + pw + mx + mn ))

    (( i++ ))
done

elapsed=$(( $(date +%s%3N) - start ))

echo "Iterations: $ITERATIONS"
echo "Result (checksum): $acc"
echo "Time: $elapsed ms"
