#!/usr/bin/env bash
# =============================================================================
# Benchmark: Text Processing (Bash equivalent)
# Mirrors bench_text_processing.stash. Generates synthetic log lines in
# memory, then filters/parses/aggregates them. Uses associative arrays for
# group-by counting.
# =============================================================================

LINES=50000
SCANS=20

build_log() {
    local n=$1
    local levels=("INFO" "WARN" "ERROR" "DEBUG" "INFO" "ERROR")
    local mods=("auth" "db" "net" "cache" "api" "fs")
    local users=("alice" "bob" "carol" "dave" "eve")
    local i lvl mn u ts
    LOG_LINES=()
    for (( i=0; i<n; i++ )); do
        lvl=${levels[$(( i % 6 ))]}
        mn=${mods[$(( i % 6 ))]}
        u=${users[$(( i % 5 ))]}
        ts="2026-01-15 12:$(( (i / 60) % 60 )):$(( i % 60 ))"
        LOG_LINES+=("$ts $lvl [$mn] event=$i user=$u")
    done
}

echo "=== Benchmark: Text Processing ==="

build_log "$LINES"

start=$(date +%s%3N)
total_errors=0
total_chars=0

for (( s=0; s<SCANS; s++ )); do
    declare -A by_module=()
    declare -A by_user=()
    errors=0

    for line in "${LOG_LINES[@]}"; do
        if [[ "$line" == *"ERROR"* ]]; then
            (( errors++ ))
            (( total_chars += ${#line} ))

            # Extract module name between [ and ]
            tmp="${line#*[}"
            modname="${tmp%%]*}"

            # Extract user value after user=
            user="${line##*user=}"

            by_module[$modname]=$(( ${by_module[$modname]:-0} + 1 ))
            by_user[$user]=$(( ${by_user[$user]:-0} + 1 ))
        fi
    done

    (( total_errors += errors ))

    for m in "${!by_module[@]}"; do
        n=${by_module[$m]:-0}
        up="${m^^}"
        rp="${up//_/-}"
        (( total_chars += ${#rp} + n ))
    done

    unset by_module by_user
done

elapsed=$(( $(date +%s%3N) - start ))

echo "Lines: $LINES, Scans: $SCANS"
echo "Total errors matched: $total_errors"
echo "Result (checksum): $total_chars"
echo "Time: $elapsed ms"
