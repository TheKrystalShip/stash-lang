# Ruby equivalent of bench_scope_lookup.stash
# Performs EXACTLY the same operations with the same iteration counts.
# Uses lambdas to replicate the nested function / closure scope chain.

def depth1
  a1 = 1
  a2 = 2
  depth2 = lambda do
    b1 = 3
    b2 = 4
    depth3 = lambda do
      c1 = 5
      c2 = 6
      depth4 = lambda do
        d1 = 7
        d2 = 8
        depth5 = lambda do
          # Walks 4 levels up the closure chain for each variable
          a1 + a2 + b1 + b2 + c1 + c2 + d1 + d2
        end
        depth5.call
      end
      depth4.call
    end
    depth3.call
  end
  depth2.call
end

ITERATIONS = 100000

puts "=== Benchmark: Scope Lookup ==="

start = Process.clock_gettime(Process::CLOCK_MONOTONIC)
i = 0
sum = 0
while i < ITERATIONS
  sum = sum + depth1()
  i += 1
end
elapsed = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - start) * 1000).to_i

puts "Iterations: #{ITERATIONS}"
puts "Result (checksum): #{sum}"
puts "Time: #{elapsed} ms"
