# Ruby equivalent of bench_algorithms.stash
# Performs EXACTLY the same operations with the same iteration counts.

# --- Recursive Fibonacci ---

def fibonacci(n)
  return 0 if n <= 0
  return 1 if n == 1
  fibonacci(n - 1) + fibonacci(n - 2)
end

# --- Bubble Sort ---

def bubble_sort(arr)
  n = arr.length
  swapped = true
  while swapped
    swapped = false
    j = 0
    while j < n - 1
      if arr[j] > arr[j + 1]
        tmp = arr[j]
        arr[j] = arr[j + 1]
        arr[j + 1] = tmp
        swapped = true
      end
      j += 1
    end
    n -= 1
  end
  arr
end

# --- Binary Search (requires sorted array) ---

def binary_search(arr, target)
  lo = 0
  hi = arr.length - 1
  while lo <= hi
    mid = (lo + hi) / 2
    return mid if arr[mid] == target
    if arr[mid] < target
      lo = mid + 1
    else
      hi = mid - 1
    end
  end
  -1
end

# --- Node struct ---

Node = Struct.new(:value, :index)

def build_nodes(count)
  nodes = []
  i = 0
  while i < count
    nodes << Node.new(i * 3 + 1, i)
    i += 1
  end
  nodes
end

def sum_nodes(nodes)
  total = 0
  i = 0
  while i < nodes.length
    total += nodes[i].value
    i += 1
  end
  total
end

# =============================================================================
# Run benchmarks
# =============================================================================

puts "=== Benchmark: Algorithms ==="
puts ""

# 1. Fibonacci(26) — deep recursion tree

t0 = Process.clock_gettime(Process::CLOCK_MONOTONIC)
fib_result = fibonacci(26)
fib_time = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - t0) * 1000).to_i

puts "fibonacci(26)      = #{fib_result}"
puts "Time: #{fib_time} ms"
puts ""

# 2. Bubble sort on 1000-element descending array (worst case)

sort_data = []
si = 1000
while si > 0
  sort_data << si
  si -= 1
end

t1 = Process.clock_gettime(Process::CLOCK_MONOTONIC)
sorted = bubble_sort(sort_data)
sort_time = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - t1) * 1000).to_i

puts "bubble_sort(1000)  first=#{sorted[0]}, last=#{sorted[sorted.length - 1]}"
puts "Time: #{sort_time} ms"
puts ""

# 3. Binary search — 10,000 searches on the sorted array

t2 = Process.clock_gettime(Process::CLOCK_MONOTONIC)
found_count = 0
bi = 0
while bi < 10000
  target = (bi % 1000) + 1
  idx = binary_search(sorted, target)
  found_count += 1 if idx >= 0
  bi += 1
end
search_time = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - t2) * 1000).to_i

puts "binary_search x10000  found=#{found_count}"
puts "Time: #{search_time} ms"
puts ""

# 4. Node build + aggregate — 5000 Node instances

t3 = Process.clock_gettime(Process::CLOCK_MONOTONIC)
nodes = build_nodes(5000)
node_sum = sum_nodes(nodes)
struct_time = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - t3) * 1000).to_i

puts "build+sum nodes(5000) sum=#{node_sum}"
puts "Time: #{struct_time} ms"
puts ""

total_time = fib_time + sort_time + search_time + struct_time
puts "Total time: #{total_time} ms"
