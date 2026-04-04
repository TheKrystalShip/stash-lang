# Python equivalent of bench_algorithms.stash
# Performs the same operations with the same iteration counts.
import time


# --- Recursive Fibonacci ---

def fibonacci(n):
    if n <= 0:
        return 0
    if n == 1:
        return 1
    return fibonacci(n - 1) + fibonacci(n - 2)


# --- Bubble Sort ---

def bubble_sort(arr):
    n = len(arr)
    swapped = True
    while swapped:
        swapped = False
        j = 0
        while j < n - 1:
            if arr[j] > arr[j + 1]:
                tmp = arr[j]
                arr[j] = arr[j + 1]
                arr[j + 1] = tmp
                swapped = True
            j += 1
        n -= 1
    return arr


# --- Binary Search (requires sorted array) ---

def binary_search(arr, target):
    lo = 0
    hi = len(arr) - 1
    while lo <= hi:
        mid = (lo + hi) // 2
        if arr[mid] == target:
            return mid
        if arr[mid] < target:
            lo = mid + 1
        else:
            hi = mid - 1
    return -1


# --- Node class (equivalent of Stash struct) ---

class Node:
    def __init__(self, value, index):
        self.value = value
        self.index = index


def build_nodes(count):
    nodes = []
    i = 0
    while i < count:
        n = Node(value=i * 3 + 1, index=i)
        nodes.append(n)
        i += 1
    return nodes


def sum_nodes(nodes):
    total = 0
    for n in nodes:
        total = total + n.value
    return total


# =============================================================================
# Run benchmarks
# =============================================================================

print("=== Benchmark: Algorithms ===")
print("")

# 1. Fibonacci(26) — deep recursion tree

t0 = time.time()

fib_result = fibonacci(26)

fib_time = int((time.time() - t0) * 1000)
print(f"fibonacci(26)      = {fib_result}")
print(f"Time: {fib_time} ms")
print("")

# 2. Bubble sort on 1000-element descending array (worst case)

sort_data = []
si = 1000
while si > 0:
    sort_data.append(si)
    si -= 1

t1 = time.time()

sorted_arr = bubble_sort(sort_data)

sort_time = int((time.time() - t1) * 1000)
print(f"bubble_sort(1000)  first={sorted_arr[0]}, last={sorted_arr[len(sorted_arr)-1]}")
print(f"Time: {sort_time} ms")
print("")

# 3. Binary search — 10,000 searches on the sorted array

t2 = time.time()

found_count = 0
bi = 0
while bi < 10000:
    target = (bi % 1000) + 1
    idx = binary_search(sorted_arr, target)
    if idx >= 0:
        found_count += 1
    bi += 1

search_time = int((time.time() - t2) * 1000)
print(f"binary_search x10000  found={found_count}")
print(f"Time: {search_time} ms")
print("")

# 4. Node build + aggregate — 5000 Node instances

t3 = time.time()

nodes = build_nodes(5000)
node_sum = sum_nodes(nodes)

struct_time = int((time.time() - t3) * 1000)
print(f"build+sum nodes(5000) sum={node_sum}")
print(f"Time: {struct_time} ms")
print("")

total_time = fib_time + sort_time + search_time + struct_time
print(f"Total time: {total_time} ms")
