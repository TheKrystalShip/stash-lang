#!/usr/bin/env bash
# =============================================================================
# Benchmark Runner — runs all benchmarks across languages, 3 runs each,
# extracts the "Time:" line and reports the median.
# =============================================================================

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
RUNS=3

# Ensure dotnet project is built
echo "Building Stash..."
dotnet build "$PROJECT_DIR" -c Release --nologo -v quiet 2>/dev/null

# Helper: extract the "Time: NNN ms" or "Total time: NNN ms" value from output
extract_time() {
    local pattern="$1"
    grep -oP "${pattern}\s*:\s*\K[0-9]+" | head -1
}

# Helper: run a command N times, collect timing, report median
run_benchmark() {
    local label="$1"
    local time_pattern="$2"
    shift 2
    local cmd=("$@")
    local times=()

    for ((r=1; r<=RUNS; r++)); do
        local output
        output=$("${cmd[@]}" 2>&1)
        local t
        t=$(echo "$output" | extract_time "$time_pattern")
        if [[ -z "$t" ]]; then
            echo "  WARNING: could not extract time from $label run $r"
            echo "  Output: $output" | head -5
            return 1
        fi
        times+=("$t")
    done

    # Sort and pick median (index 1 of 3)
    IFS=$'\n' sorted=($(sort -n <<<"${times[*]}")); unset IFS
    local median=${sorted[1]}
    echo "$median"
}

# Formatting helper
fmt() {
    # Right-align number with comma separators in a field
    printf "%'d ms" "$1"
}

echo ""
echo "Running each benchmark $RUNS times, reporting median..."
echo "========================================================"
echo ""

# --- Algorithms ---
echo ">>> Algorithms"
declare -A algo_results
for lang_label_cmd in \
    "Stash:dotnet run --project $PROJECT_DIR/Stash.Cli/ -c Release -- $SCRIPT_DIR/bench_algorithms.stash" \
    "Python:python3 $SCRIPT_DIR/bench_algorithms.py" \
    "Node.js:node $SCRIPT_DIR/bench_algorithms.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_algorithms.rb" \
    "Perl:perl $SCRIPT_DIR/bench_algorithms.pl" \
    "Lua:lua $SCRIPT_DIR/bench_algorithms.lua" \
    "Bash:bash $SCRIPT_DIR/bench_algorithms.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    printf "  %-10s " "$lang"
    median=$(run_benchmark "$lang" "Total time" $cmd)
    algo_results["$lang"]=$median
    echo "$(fmt "$median")"
done

echo ""

# --- Function Calls ---
echo ">>> Function Calls"
declare -A func_results
for lang_label_cmd in \
    "Stash:dotnet run --project $PROJECT_DIR/Stash.Cli/ -c Release -- $SCRIPT_DIR/bench_function_calls.stash" \
    "Python:python3 $SCRIPT_DIR/bench_function_calls.py" \
    "Node.js:node $SCRIPT_DIR/bench_function_calls.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_function_calls.rb" \
    "Perl:perl $SCRIPT_DIR/bench_function_calls.pl" \
    "Lua:lua $SCRIPT_DIR/bench_function_calls.lua" \
    "Bash:bash $SCRIPT_DIR/bench_function_calls.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    printf "  %-10s " "$lang"
    median=$(run_benchmark "$lang" "^Time" $cmd)
    func_results["$lang"]=$median
    echo "$(fmt "$median")"
done

echo ""

# --- Expression Throughput ---
echo ">>> Expression Throughput"
declare -A expr_results
for lang_label_cmd in \
    "Stash:dotnet run --project $PROJECT_DIR/Stash.Cli/ -c Release -- $SCRIPT_DIR/bench_lexer_heavy.stash" \
    "Python:python3 $SCRIPT_DIR/bench_lexer_heavy.py" \
    "Node.js:node $SCRIPT_DIR/bench_lexer_heavy.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_lexer_heavy.rb" \
    "Perl:perl $SCRIPT_DIR/bench_lexer_heavy.pl" \
    "Lua:lua $SCRIPT_DIR/bench_lexer_heavy.lua" \
    "Bash:bash $SCRIPT_DIR/bench_lexer_heavy.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    printf "  %-10s " "$lang"
    median=$(run_benchmark "$lang" "^Time" $cmd)
    expr_results["$lang"]=$median
    echo "$(fmt "$median")"
done

echo ""

# --- Built-in Functions ---
echo ">>> Built-in Functions"
declare -A ns_results
for lang_label_cmd in \
    "Stash:dotnet run --project $PROJECT_DIR/Stash.Cli/ -c Release -- $SCRIPT_DIR/bench_namespace_calls.stash" \
    "Python:python3 $SCRIPT_DIR/bench_namespace_calls.py" \
    "Node.js:node $SCRIPT_DIR/bench_namespace_calls.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_namespace_calls.rb" \
    "Perl:perl $SCRIPT_DIR/bench_namespace_calls.pl" \
    "Lua:lua $SCRIPT_DIR/bench_namespace_calls.lua" \
    "Bash:bash $SCRIPT_DIR/bench_namespace_calls.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    printf "  %-10s " "$lang"
    median=$(run_benchmark "$lang" "^Time" $cmd)
    ns_results["$lang"]=$median
    echo "$(fmt "$median")"
done

echo ""

# --- Scope Lookup ---
echo ">>> Scope Lookup"
declare -A scope_results
for lang_label_cmd in \
    "Stash:dotnet run --project $PROJECT_DIR/Stash.Cli/ -c Release -- $SCRIPT_DIR/bench_scope_lookup.stash" \
    "Python:python3 $SCRIPT_DIR/bench_scope_lookup.py" \
    "Node.js:node $SCRIPT_DIR/bench_scope_lookup.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_scope_lookup.rb" \
    "Perl:perl $SCRIPT_DIR/bench_scope_lookup.pl" \
    "Lua:lua $SCRIPT_DIR/bench_scope_lookup.lua" \
    "Bash:bash $SCRIPT_DIR/bench_scope_lookup.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    printf "  %-10s " "$lang"
    median=$(run_benchmark "$lang" "^Time" $cmd)
    scope_results["$lang"]=$median
    echo "$(fmt "$median")"
done

echo ""
echo "========================================================"
echo "SUMMARY TABLE (all times in ms, median of $RUNS runs)"
echo "========================================================"
printf "%-26s %10s %10s %10s %10s %10s %10s %10s\n" "Benchmark" "Stash" "Python" "Node.js" "Ruby" "Perl" "Lua" "Bash"
printf "%-26s %10s %10s %10s %10s %10s %10s %10s\n" "--------------------------" "--------" "--------" "--------" "--------" "--------" "--------" "--------"
printf "%-26s %10s %10s %10s %10s %10s %10s %10s\n" "Algorithms"        "${algo_results[Stash]}"  "${algo_results[Python]}"  "${algo_results[Node.js]}"  "${algo_results[Ruby]}"  "${algo_results[Perl]}"  "${algo_results[Lua]}"  "${algo_results[Bash]}"
printf "%-26s %10s %10s %10s %10s %10s %10s %10s\n" "Function Calls"    "${func_results[Stash]}"  "${func_results[Python]}"  "${func_results[Node.js]}"  "${func_results[Ruby]}"  "${func_results[Perl]}"  "${func_results[Lua]}"  "${func_results[Bash]}"
printf "%-26s %10s %10s %10s %10s %10s %10s %10s\n" "Expression Throughput" "${expr_results[Stash]}"  "${expr_results[Python]}"  "${expr_results[Node.js]}"  "${expr_results[Ruby]}"  "${expr_results[Perl]}"  "${expr_results[Lua]}"  "${expr_results[Bash]}"
printf "%-26s %10s %10s %10s %10s %10s %10s %10s\n" "Built-in Functions" "${ns_results[Stash]}"    "${ns_results[Python]}"    "${ns_results[Node.js]}"    "${ns_results[Ruby]}"    "${ns_results[Perl]}"    "${ns_results[Lua]}"    "${ns_results[Bash]}"
printf "%-26s %10s %10s %10s %10s %10s %10s %10s\n" "Scope Lookup"      "${scope_results[Stash]}" "${scope_results[Python]}" "${scope_results[Node.js]}" "${scope_results[Ruby]}" "${scope_results[Perl]}" "${scope_results[Lua]}" "${scope_results[Bash]}"
echo ""
