#!/usr/bin/env bash
# =============================================================================
# Benchmark: Data Transformation (Bash equivalent)
# Mirrors bench_data_transform.stash. Generates synthetic CSV-style rows in
# memory, then parses them, groups by department, computes aggregates, and
# serializes the result back to a key=value summary.
# =============================================================================

ROWS=30000
PASSES=8

build_rows() {
    local n=$1
    local depts=("eng" "sales" "ops" "support" "eng" "sales")
    local names=("Ada" "Ben" "Cyd" "Dan" "Eli" "Fay" "Gus" "Hal")
    local i d nm salary
    ROW_LIST=()
    for (( i=0; i<n; i++ )); do
        d=${depts[$(( i % 6 ))]}
        nm=${names[$(( i % 8 ))]}
        salary=$(( 30000 + ((i * 137) % 70000) ))
        ROW_LIST+=("$i,$nm,$d,$salary")
    done
}

echo "=== Benchmark: Data Transformation ==="

build_rows "$ROWS"

start=$(date +%s%3N)
total_summary_chars=0
total_count=0

for (( p=0; p<PASSES; p++ )); do
    declare -A counts=()
    declare -A sums=()
    declare -A maxs=()

    for row in "${ROW_LIST[@]}"; do
        IFS=',' read -ra parts <<< "$row"
        dept=${parts[2]}
        salary=${parts[3]}

        counts[$dept]=$(( ${counts[$dept]:-0} + 1 ))
        sums[$dept]=$(( ${sums[$dept]:-0} + salary ))

        mx=${maxs[$dept]:-0}
        if (( salary > mx )); then
            maxs[$dept]=$salary
        fi

        (( total_count++ ))
    done

    parts_out=()
    for k in "${!counts[@]}"; do
        c=${counts[$k]:-0}
        s=${sums[$k]:-0}
        mx=${maxs[$k]:-0}
        avg=$(( s / c ))
        line="${k}=count:${c},sum:${s},avg:${avg},max:${mx}"
        parts_out+=("$line")
    done
    summary=$(IFS=';'; echo "${parts_out[*]}")
    (( total_summary_chars += ${#summary} ))

    unset counts sums maxs
done

elapsed=$(( $(date +%s%3N) - start ))

echo "Rows: $ROWS, Passes: $PASSES"
echo "Total rows processed: $total_count"
echo "Result (checksum): $total_summary_chars"
echo "Time: $elapsed ms"
