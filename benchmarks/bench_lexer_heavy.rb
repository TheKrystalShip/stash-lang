# Ruby equivalent of bench_lexer_heavy.stash
# Performs EXACTLY the same operations with the same iteration counts.

# 60 variables with varied numeric literals
v01 = 1001;     v02 = 1002.5;   v03 = 2003;     v04 = 3004.25
v05 = 5005;     v06 = 6006.75;  v07 = 7007;     v08 = 8008.125
v09 = 9009;     v10 = 1010.5;   v11 = 1111;     v12 = 1212.25
v13 = 1313;     v14 = 1414.75;  v15 = 1515;     v16 = 1616.5
v17 = 1717;     v18 = 1818.125; v19 = 1919;     v20 = 2020.25
v21 = 2121;     v22 = 2222.5;   v23 = 2323;     v24 = 2424.75
v25 = 2525;     v26 = 2626.125; v27 = 2727;     v28 = 2828.25
v29 = 2929;     v30 = 3030.5;   v31 = 3131;     v32 = 3232.75
v33 = 3333;     v34 = 3434.125; v35 = 3535;     v36 = 3636.25
v37 = 3737;     v38 = 3838.5;   v39 = 3939;     v40 = 4040.75
v41 = 4141;     v42 = 4242.125; v43 = 4343;     v44 = 4444.25
v45 = 4545;     v46 = 4646.5;   v47 = 4747;     v48 = 4848.75
v49 = 4949;     v50 = 5050.125; v51 = 5151;     v52 = 5252.25
v53 = 5353;     v54 = 5454.5;   v55 = 5555;     v56 = 5656.75
v57 = 5757;     v58 = 5858.125; v59 = 5959;     v60 = 6060.25

# String literals
s01 = "alpha";   s02 = "bravo";   s03 = "charlie"; s04 = "delta"
s05 = "echo";    s06 = "foxtrot"; s07 = "golf";    s08 = "hotel"
s09 = "india";   s10 = "juliet"

def compute(n,
            v01, v02, v03, v04, v05, v06, v07, v08, v09, v10,
            v11, v12, v13, v14, v15, v16, v17, v18, v19, v20,
            v21, v22, v23, v24, v25, v26, v27, v28, v29, v30,
            v31, v32, v33, v34, v35, v36, v37, v38, v39, v40,
            v41, v42, v43, v44, v45, v46, v47, v48, v49, v50,
            v51, v52, v53, v54, v55, v56, v57, v58, v59, v60,
            s01, s02, s03, s04)
  r01 = v01 + v02 + v03 + v04 + v05 + v06 + v07 + v08 + v09 + v10
  r02 = v11 + v12 + v13 + v14 + v15 + v16 + v17 + v18 + v19 + v20
  r03 = v21 + v22 + v23 + v24 + v25 + v26 + v27 + v28 + v29 + v30
  r04 = v31 + v32 + v33 + v34 + v35 + v36 + v37 + v38 + v39 + v40
  r05 = v41 + v42 + v43 + v44 + v45 + v46 + v47 + v48 + v49 + v50
  r06 = v51 + v52 + v53 + v54 + v55 + v56 + v57 + v58 + v59 + v60

  tag  = "#{s01}-#{s02}-#{n}"
  tag2 = s03.upcase + "-" + s04.downcase

  mid  = (r01 + r02 + r03) - (r04 - r05 + r06)
  deep = mid * 2 + (r01 - r02) * (r03 - r04) / 1000.0

  deep + tag.length + tag2.length
end

ITERATIONS = 100000

puts "=== Benchmark: Lexer / Expression Throughput ==="

start = Process.clock_gettime(Process::CLOCK_MONOTONIC)
i = 0
acc = 0.0
while i < ITERATIONS
  acc = acc + compute(i,
                      v01, v02, v03, v04, v05, v06, v07, v08, v09, v10,
                      v11, v12, v13, v14, v15, v16, v17, v18, v19, v20,
                      v21, v22, v23, v24, v25, v26, v27, v28, v29, v30,
                      v31, v32, v33, v34, v35, v36, v37, v38, v39, v40,
                      v41, v42, v43, v44, v45, v46, v47, v48, v49, v50,
                      v51, v52, v53, v54, v55, v56, v57, v58, v59, v60,
                      s01, s02, s03, s04)
  i += 1
end
elapsed = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - start) * 1000).to_i

puts "Iterations: #{ITERATIONS}"
puts "Result (checksum): #{acc}"
puts "Time: #{elapsed} ms"
