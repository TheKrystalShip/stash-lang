#!/usr/bin/env bash
# =============================================================================
# Benchmark Runner — runs all benchmarks across languages, 3 runs each,
# extracts the "Time:" line and reports the median.
# =============================================================================

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
RUNS=3

# =============================================================================
# CPU Performance Governor
# =============================================================================
# Saves the current scaling governor for every CPU, switches to "performance"
# mode (disables P-state frequency scaling, gives maximum and deterministic
# clock speed), and restores the original governors on EXIT — even if the
# script is interrupted with Ctrl-C or killed.
#
# Requires cpupower (linux-cpupower package).  If cpupower is unavailable, or
# if elevating to root fails, the script continues with a warning so CI/CD
# pipelines are not broken.
# =============================================================================

_CPU_GOVERNORS_SAVED=()  # parallel array: index = cpu number, value = governor name
_GOVERNOR_APPLIED=0

_save_and_set_performance_governor() {
    if [[ "$(uname)" != "Linux" ]]; then
        echo "  [perf] CPU governor control is Linux-only — skipping."
        return
    fi

    if ! command -v cpupower &>/dev/null; then
        echo "  [perf] cpupower not found. Install it:"
        echo "         Debian/Ubuntu: sudo apt install linux-tools-common linux-tools-\$(uname -r)"
        echo "         Fedora/RHEL:   sudo dnf install kernel-tools"
        echo "         Arch:          sudo pacman -S cpupower"
        echo "         Running without governor lock."
        echo "         WARNING: CPU frequency scaling may cause non-reproducible results."
        return
    fi

    # Collect current governor for every online CPU so we can restore later.
    local cpu_count
    cpu_count=$(nproc)
    for ((cpu=0; cpu<cpu_count; cpu++)); do
        local gov_file="/sys/devices/system/cpu/cpu${cpu}/cpufreq/scaling_governor"
        if [[ -r "$gov_file" ]]; then
            _CPU_GOVERNORS_SAVED[$cpu]=$(< "$gov_file")
        else
            _CPU_GOVERNORS_SAVED[$cpu]="unknown"
        fi
    done

    # Attempt to switch to performance governor.
    local cpupower_cmd="cpupower frequency-set -g performance"
    if cpupower frequency-set -g performance &>/dev/null; then
        _GOVERNOR_APPLIED=1
        echo "  [perf] CPU governor → performance (was: ${_CPU_GOVERNORS_SAVED[0]})"
    elif sudo -n cpupower frequency-set -g performance &>/dev/null 2>&1; then
        _GOVERNOR_APPLIED=2  # elevated via passwordless sudo
        echo "  [perf] CPU governor → performance via sudo (was: ${_CPU_GOVERNORS_SAVED[0]})"
    else
        echo "  [perf] Could not set performance governor (need root or passwordless sudo)."
        echo "         WARNING: CPU frequency scaling may cause non-reproducible results."
        echo "         To fix: run as root, or add 'cpupower' to sudoers NOPASSWD."
    fi
}

_restore_governors() {
    if [[ $_GOVERNOR_APPLIED -eq 0 ]]; then return; fi

    local orig="${_CPU_GOVERNORS_SAVED[0]:-powersave}"
    local restore_cmd="cpupower frequency-set -g $orig"

    if [[ $_GOVERNOR_APPLIED -eq 1 ]]; then
        $restore_cmd &>/dev/null || true
    else
        sudo -n $restore_cmd &>/dev/null 2>&1 || true
    fi
    echo "  [perf] CPU governor restored → $orig"
}

# Register cleanup so the governor is always restored.
trap '_restore_governors' EXIT

# --- Language filter (optional first argument) ---
LANG_FILTER=""
if [[ $# -gt 0 ]]; then
    case "${1,,}" in
        stash)            LANG_FILTER="Stash"   ;;
        python)           LANG_FILTER="Python"  ;;
        node|node.js|js)  LANG_FILTER="Node.js" ;;
        ruby)             LANG_FILTER="Ruby"    ;;
        perl)             LANG_FILTER="Perl"    ;;
        lua)              LANG_FILTER="Lua"     ;;
        bash)             LANG_FILTER="Bash"    ;;
        all)              LANG_FILTER=""        ;;
        *)
            echo "Usage: $0 [stash|python|node|ruby|perl|lua|bash|all]" >&2
            exit 1
            ;;
    esac
fi
should_run() { [[ -z "$LANG_FILTER" || "$1" == "$LANG_FILTER" ]]; }

# Switch CPU to maximum-performance mode before building or running anything.
echo "Configuring CPU governor..."
_save_and_set_performance_governor

# Ensure dotnet project is built
echo "Building Stash (AOT)..."
dotnet publish "$PROJECT_DIR/Stash.Cli/" -c Release --nologo -v quiet -o "$PROJECT_DIR/.bench-bin" 2>/dev/null
STASH_BIN="$PROJECT_DIR/.bench-bin/Stash"

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
    "Stash:$STASH_BIN $SCRIPT_DIR/bench_algorithms.stash" \
    "Python:python3 $SCRIPT_DIR/bench_algorithms.py" \
    "Node.js:node $SCRIPT_DIR/bench_algorithms.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_algorithms.rb" \
    "Perl:perl $SCRIPT_DIR/bench_algorithms.pl" \
    "Lua:lua $SCRIPT_DIR/bench_algorithms.lua" \
    "Bash:bash $SCRIPT_DIR/bench_algorithms.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    if should_run "$lang"; then
        printf "  %-10s " "$lang"
        median=$(run_benchmark "$lang" "Total time" $cmd)
        algo_results["$lang"]=$median
        echo "$(fmt "$median")"
    fi
done

echo ""

# --- Function Calls ---
echo ">>> Function Calls"
declare -A func_results
for lang_label_cmd in \
    "Stash:$STASH_BIN $SCRIPT_DIR/bench_function_calls.stash" \
    "Python:python3 $SCRIPT_DIR/bench_function_calls.py" \
    "Node.js:node $SCRIPT_DIR/bench_function_calls.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_function_calls.rb" \
    "Perl:perl $SCRIPT_DIR/bench_function_calls.pl" \
    "Lua:lua $SCRIPT_DIR/bench_function_calls.lua" \
    "Bash:bash $SCRIPT_DIR/bench_function_calls.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    if should_run "$lang"; then
        printf "  %-10s " "$lang"
        median=$(run_benchmark "$lang" "^Time" $cmd)
        func_results["$lang"]=$median
        echo "$(fmt "$median")"
    fi
done

echo ""

# --- Expression Throughput ---
echo ">>> Expression Throughput"
declare -A expr_results
for lang_label_cmd in \
    "Stash:$STASH_BIN $SCRIPT_DIR/bench_lexer_heavy.stash" \
    "Python:python3 $SCRIPT_DIR/bench_lexer_heavy.py" \
    "Node.js:node $SCRIPT_DIR/bench_lexer_heavy.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_lexer_heavy.rb" \
    "Perl:perl $SCRIPT_DIR/bench_lexer_heavy.pl" \
    "Lua:lua $SCRIPT_DIR/bench_lexer_heavy.lua" \
    "Bash:bash $SCRIPT_DIR/bench_lexer_heavy.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    if should_run "$lang"; then
        printf "  %-10s " "$lang"
        median=$(run_benchmark "$lang" "^Time" $cmd)
        expr_results["$lang"]=$median
        echo "$(fmt "$median")"
    fi
done

echo ""

# --- Built-in Functions ---
echo ">>> Built-in Functions"
declare -A ns_results
for lang_label_cmd in \
    "Stash:$STASH_BIN $SCRIPT_DIR/bench_namespace_calls.stash" \
    "Python:python3 $SCRIPT_DIR/bench_namespace_calls.py" \
    "Node.js:node $SCRIPT_DIR/bench_namespace_calls.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_namespace_calls.rb" \
    "Perl:perl $SCRIPT_DIR/bench_namespace_calls.pl" \
    "Lua:lua $SCRIPT_DIR/bench_namespace_calls.lua" \
    "Bash:bash $SCRIPT_DIR/bench_namespace_calls.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    if should_run "$lang"; then
        printf "  %-10s " "$lang"
        median=$(run_benchmark "$lang" "^Time" $cmd)
        ns_results["$lang"]=$median
        echo "$(fmt "$median")"
    fi
done

echo ""

# --- Scope Lookup ---
echo ">>> Scope Lookup"
declare -A scope_results
for lang_label_cmd in \
    "Stash:$STASH_BIN $SCRIPT_DIR/bench_scope_lookup.stash" \
    "Python:python3 $SCRIPT_DIR/bench_scope_lookup.py" \
    "Node.js:node $SCRIPT_DIR/bench_scope_lookup.js" \
    "Ruby:ruby $SCRIPT_DIR/bench_scope_lookup.rb" \
    "Perl:perl $SCRIPT_DIR/bench_scope_lookup.pl" \
    "Lua:lua $SCRIPT_DIR/bench_scope_lookup.lua" \
    "Bash:bash $SCRIPT_DIR/bench_scope_lookup.sh"; do
    lang="${lang_label_cmd%%:*}"
    cmd="${lang_label_cmd#*:}"
    if should_run "$lang"; then
        printf "  %-10s " "$lang"
        median=$(run_benchmark "$lang" "^Time" $cmd)
        scope_results["$lang"]=$median
        echo "$(fmt "$median")"
    fi
done

echo ""
echo "========================================================"
echo "SUMMARY TABLE (all times in ms, median of $RUNS runs)"
echo "========================================================"
if [[ -z "$LANG_FILTER" ]]; then
    printf "%-26s %12s %12s %10s %10s %10s %10s %10s %10s\n" "Benchmark" "Stash" "Python" "Node.js" "Ruby" "Perl" "Lua" "Bash"
    printf "%-26s %12s %12s %10s %10s %10s %10s %10s %10s\n" "--------------------------" "----------" "--------" "--------" "--------" "--------" "--------" "--------"
    printf "%-26s %12s %12s %10s %10s %10s %10s %10s %10s\n" "Algorithms"            "${algo_results[Stash]}"  "${algo_results[Python]}"  "${algo_results[Node.js]}"  "${algo_results[Ruby]}"  "${algo_results[Perl]}"  "${algo_results[Lua]}"  "${algo_results[Bash]}"
    printf "%-26s %12s %12s %10s %10s %10s %10s %10s %10s\n" "Function Calls"        "${func_results[Stash]}"  "${func_results[Python]}"  "${func_results[Node.js]}"  "${func_results[Ruby]}"  "${func_results[Perl]}"  "${func_results[Lua]}"  "${func_results[Bash]}"
    printf "%-26s %12s %12s %10s %10s %10s %10s %10s %10s\n" "Expression Throughput" "${expr_results[Stash]}"  "${expr_results[Python]}"  "${expr_results[Node.js]}"  "${expr_results[Ruby]}"  "${expr_results[Perl]}"  "${expr_results[Lua]}"  "${expr_results[Bash]}"
    printf "%-26s %12s %12s %10s %10s %10s %10s %10s %10s\n" "Built-in Functions"    "${ns_results[Stash]}"    "${ns_results[Python]}"    "${ns_results[Node.js]}"    "${ns_results[Ruby]}"    "${ns_results[Perl]}"    "${ns_results[Lua]}"    "${ns_results[Bash]}"
    printf "%-26s %12s %12s %10s %10s %10s %10s %10s %10s\n" "Scope Lookup"          "${scope_results[Stash]}" "${scope_results[Python]}" "${scope_results[Node.js]}" "${scope_results[Ruby]}" "${scope_results[Perl]}" "${scope_results[Lua]}" "${scope_results[Bash]}"
else
    printf "%-26s %12s\n" "Benchmark" "$LANG_FILTER"
    printf "%-26s %12s\n" "--------------------------" "----------"
    printf "%-26s %12s\n" "Algorithms"            "${algo_results[$LANG_FILTER]:-n/a}"
    printf "%-26s %12s\n" "Function Calls"        "${func_results[$LANG_FILTER]:-n/a}"
    printf "%-26s %12s\n" "Expression Throughput" "${expr_results[$LANG_FILTER]:-n/a}"
    printf "%-26s %12s\n" "Built-in Functions"    "${ns_results[$LANG_FILTER]:-n/a}"
    printf "%-26s %12s\n" "Scope Lookup"          "${scope_results[$LANG_FILTER]:-n/a}"
fi
echo ""
