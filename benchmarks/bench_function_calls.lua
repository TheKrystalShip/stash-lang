-- Lua equivalent of bench_function_calls.py
-- Performs the same operations with the same iteration counts.


function zero_args()
    return 42
end

function one_arg(x)
    return x + 1
end

function two_args(a, b)
    return a + b
end

function three_args(a, b, c)
    return a + b + c
end

function four_args(a, b, c, d)
    return a + b + c + d
end

function compute(x)
    local t = x * 3
    local u = t - x
    return u + 1
end

local ITERATIONS = 100000
print("=== Benchmark: Function Calls ===")

local start = os.clock()

local i = 0
local acc = 0

while i < ITERATIONS do
    acc = acc + zero_args()
    acc = acc + one_arg(i)
    acc = acc + two_args(i, 2)
    acc = acc + three_args(i, 2, 3)
    acc = acc + four_args(i, 1, 2, 3)
    acc = acc + compute(i)
    i = i + 1
end

local elapsed = math.floor((os.clock() - start) * 1000)
print("Iterations: " .. ITERATIONS)
print("Result (checksum): " .. acc)
print("Time: " .. elapsed .. " ms")
