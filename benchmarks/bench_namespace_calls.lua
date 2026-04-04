-- Lua equivalent of bench_namespace_calls.py
-- Performs the same operations with the same iteration counts.

local ITERATIONS = 200000

print("=== Benchmark: Namespace Calls ===")

local start = os.clock()
local i = 0
local acc = 0.0
while i < ITERATIONS do
    local sq  = math.sqrt(i + 1)
    local ab  = math.abs(-i - 1)
    local fl  = math.floor(sq + 0.5)
    local pw  = 2 ^ 8
    local mx  = math.max(i, i + 1)
    local mn  = math.min(i, i + 1)
    local up  = string.upper("hello")
    local lo  = string.lower("WORLD")
    local tr  = ("  stash  "):match("^%s*(.-)%s*$")
    local rp  = string.gsub("abc", "b", "B")
    local cs  = tostring(i)
    local ci  = tonumber("42")
    local chx = string.format("0x%x", 255)
    acc = acc + sq + ab + fl + pw + mx + mn
    i = i + 1
end
local elapsed = math.floor((os.clock() - start) * 1000)

print("Iterations: " .. ITERATIONS)
print("Result (checksum): " .. acc)
print("Time: " .. elapsed .. " ms")
