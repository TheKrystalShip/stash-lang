-- Lua equivalent of bench_algorithms.py
-- Performs the same operations with the same iteration counts.


-- --- Recursive Fibonacci ---

function fibonacci(n)
    if n <= 0 then return 0 end
    if n == 1 then return 1 end
    return fibonacci(n - 1) + fibonacci(n - 2)
end


-- --- Bubble Sort ---

function bubble_sort(arr)
    local n = #arr
    local swapped = true
    while swapped do
        swapped = false
        local j = 1
        while j < n do
            if arr[j] > arr[j + 1] then
                local tmp = arr[j]
                arr[j] = arr[j + 1]
                arr[j + 1] = tmp
                swapped = true
            end
            j = j + 1
        end
        n = n - 1
    end
    return arr
end


-- --- Binary Search (requires sorted array) ---

function binary_search(arr, target)
    local lo = 1
    local hi = #arr
    while lo <= hi do
        local mid = math.floor((lo + hi) / 2)
        if arr[mid] == target then
            return mid
        end
        if arr[mid] < target then
            lo = mid + 1
        else
            hi = mid - 1
        end
    end
    return -1
end


-- --- Node struct (equivalent of Stash struct) ---

function build_nodes(count)
    local nodes = {}
    local i = 0
    while i < count do
        nodes[#nodes + 1] = {value = i * 3 + 1, index = i}
        i = i + 1
    end
    return nodes
end

function sum_nodes(nodes)
    local total = 0
    local i = 1
    while i <= #nodes do
        total = total + nodes[i].value
        i = i + 1
    end
    return total
end


-- =============================================================================
-- Run benchmarks
-- =============================================================================

print("=== Benchmark: Algorithms ===")
print("")

-- 1. Fibonacci(26) — deep recursion tree

local t0 = os.clock()

local fib_result = fibonacci(26)

local fib_time = math.floor((os.clock() - t0) * 1000)
print("fibonacci(26)      = " .. fib_result)
print("Time: " .. fib_time .. " ms")
print("")

-- 2. Bubble sort on 1000-element descending array (worst case)

local sort_data = {}
local si = 1000
while si > 0 do
    sort_data[#sort_data + 1] = si
    si = si - 1
end

local t1 = os.clock()

local sorted_arr = bubble_sort(sort_data)

local sort_time = math.floor((os.clock() - t1) * 1000)
print("bubble_sort(1000)  first=" .. sorted_arr[1] .. ", last=" .. sorted_arr[#sorted_arr])
print("Time: " .. sort_time .. " ms")
print("")

-- 3. Binary search — 10,000 searches on the sorted array

local t2 = os.clock()

local found_count = 0
local bi = 0
while bi < 10000 do
    local target = (bi % 1000) + 1
    local idx = binary_search(sorted_arr, target)
    if idx >= 0 then
        found_count = found_count + 1
    end
    bi = bi + 1
end

local search_time = math.floor((os.clock() - t2) * 1000)
print("binary_search x10000  found=" .. found_count)
print("Time: " .. search_time .. " ms")
print("")

-- 4. Node build + aggregate — 5000 Node instances

local t3 = os.clock()

local nodes = build_nodes(5000)
local node_sum = sum_nodes(nodes)

local struct_time = math.floor((os.clock() - t3) * 1000)
print("build+sum nodes(5000) sum=" .. node_sum)
print("Time: " .. struct_time .. " ms")
print("")

local total_time = fib_time + sort_time + search_time + struct_time
print("Total time: " .. total_time .. " ms")
