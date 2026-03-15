#!/usr/bin/env bash
# =============================================================================
# Benchmark: Lexer / Expression Throughput (Bash equivalent)
# Mirrors bench_lexer_heavy.stash using integer truncations of all float values.
# Floating-point decimals are dropped (truncated), which is acceptable since
# the goal is to benchmark expression evaluation throughput, not numerical
# accuracy.
# =============================================================================

# 60 numeric variables (float values truncated to integers)
v01=1001;  v02=1002;  v03=2003;  v04=3004;  v05=5005;  v06=6006
v07=7007;  v08=8008;  v09=9009;  v10=1010;  v11=1111;  v12=1212
v13=1313;  v14=1414;  v15=1515;  v16=1616;  v17=1717;  v18=1818
v19=1919;  v20=2020;  v21=2121;  v22=2222;  v23=2323;  v24=2424
v25=2525;  v26=2626;  v27=2727;  v28=2828;  v29=2929;  v30=3030
v31=3131;  v32=3232;  v33=3333;  v34=3434;  v35=3535;  v36=3636
v37=3737;  v38=3838;  v39=3939;  v40=4040;  v41=4141;  v42=4242
v43=4343;  v44=4444;  v45=4545;  v46=4646;  v47=4747;  v48=4848
v49=4949;  v50=5050;  v51=5151;  v52=5252;  v53=5353;  v54=5454
v55=5555;  v56=5656;  v57=5757;  v58=5858;  v59=5959;  v60=6060

# 10 string variables
s01="alpha";   s02="bravo";   s03="charlie"; s04="delta"
s05="echo";    s06="foxtrot"; s07="golf";    s08="hotel"
s09="india";   s10="juliet"

# Global return value (avoids subshell overhead for function returns)
__ret=0

compute() {
    local n=$1

    local r01=$(( v01 + v02 + v03 + v04 + v05 + v06 + v07 + v08 + v09 + v10 ))
    local r02=$(( v11 + v12 + v13 + v14 + v15 + v16 + v17 + v18 + v19 + v20 ))
    local r03=$(( v21 + v22 + v23 + v24 + v25 + v26 + v27 + v28 + v29 + v30 ))
    local r04=$(( v31 + v32 + v33 + v34 + v35 + v36 + v37 + v38 + v39 + v40 ))
    local r05=$(( v41 + v42 + v43 + v44 + v45 + v46 + v47 + v48 + v49 + v50 ))
    local r06=$(( v51 + v52 + v53 + v54 + v55 + v56 + v57 + v58 + v59 + v60 ))

    # String interpolation: tag = "{s01}-{s02}-{n}"
    local tag="${s01}-${s02}-${n}"

    # tag2 = upper(s03) + "-" + lower(s04)  — uses bash 4+ case modification builtins
    local tag2="${s03^^}-${s04,,}"

    local mid=$(( (r01 + r02 + r03) - (r04 - r05 + r06) ))
    local deep=$(( mid * 2 + (r01 - r02) * (r03 - r04) / 1000 ))

    __ret=$(( deep + ${#tag} + ${#tag2} ))
}

ITERATIONS=100000

echo "=== Benchmark: Lexer / Expression Throughput ==="

start=$(date +%s%3N)

acc=0
i=0
while (( i < ITERATIONS )); do
    compute "$i"
    (( acc += __ret ))
    (( i++ ))
done

elapsed=$(( $(date +%s%3N) - start ))

echo "Iterations: ${ITERATIONS}"
echo "Result (checksum): ${acc}"
echo "Time: ${elapsed} ms"
