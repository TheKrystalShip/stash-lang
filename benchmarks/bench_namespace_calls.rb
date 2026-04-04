# Ruby equivalent of bench_namespace_calls.stash
# Performs EXACTLY the same operations with the same iteration counts.

ITERATIONS = 200000

puts "=== Benchmark: Namespace Calls ==="

start = Process.clock_gettime(Process::CLOCK_MONOTONIC)
i = 0
acc = 0.0
while i < ITERATIONS
  # math namespace equivalents
  sq  = Math.sqrt(i + 1)
  ab  = (-i - 1).abs
  fl  = (sq + 0.5).floor
  pw  = 2**8
  mx  = [i, i + 1].max
  mn  = [i, i + 1].min

  # str namespace equivalents
  up  = "hello".upcase
  lo  = "WORLD".downcase
  tr  = "  stash  ".strip
  rp  = "abc".sub("b", "B")

  # conv namespace equivalents
  cs  = i.to_s
  ci  = "42".to_i
  chx = 255.to_s(16)

  acc = acc + sq + ab + fl + pw + mx + mn
  i += 1
end
elapsed = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - start) * 1000).to_i

puts "Iterations: #{ITERATIONS}"
puts "Result (checksum): #{acc}"
puts "Time: #{elapsed} ms"
