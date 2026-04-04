# Ruby equivalent of bench_function_calls.stash
# Performs EXACTLY the same operations with the same iteration counts.

def zero_args
  42
end

def one_arg(x)
  x + 1
end

def two_args(a, b)
  a + b
end

def three_args(a, b, c)
  a + b + c
end

def four_args(a, b, c, d)
  a + b + c + d
end

def compute(x)
  t = x * 3
  u = t - x
  u + 1
end

ITERATIONS = 100000

puts "=== Benchmark: Function Calls ==="

start = Process.clock_gettime(Process::CLOCK_MONOTONIC)

i = 0
acc = 0

while i < ITERATIONS
  acc = acc + zero_args()
  acc = acc + one_arg(i)
  acc = acc + two_args(i, 2)
  acc = acc + three_args(i, 2, 3)
  acc = acc + four_args(i, 1, 2, 3)
  acc = acc + compute(i)
  i += 1
end

elapsed = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - start) * 1000).to_i

puts "Iterations: #{ITERATIONS}"
puts "Result (checksum): #{acc}"
puts "Time: #{elapsed} ms"
