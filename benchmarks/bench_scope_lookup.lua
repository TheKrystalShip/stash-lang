-- Lua equivalent of bench_scope_lookup.py
-- Performs the same operations with the same iteration counts.


local function depth1()
    local a1 = 1
    local a2 = 2
    local function depth2()
        local b1 = 3
        local b2 = 4
        local function depth3()
            local c1 = 5
            local c2 = 6
            local function depth4()
                local d1 = 7
                local d2 = 8
                local function depth5()
                    return a1 + a2 + b1 + b2 + c1 + c2 + d1 + d2
                end
                return depth5()
            end
            return depth4()
        end
        return depth3()
    end
    return depth2()
end

local ITERATIONS = 100000

print("=== Benchmark: Scope Lookup ===")

local start = os.clock()
local i = 0
local total = 0
while i < ITERATIONS do
    total = total + depth1()
    i = i + 1
end
local elapsed = math.floor((os.clock() - start) * 1000)

print("Iterations: " .. ITERATIONS)
print("Result (checksum): " .. total)
print("Time: " .. elapsed .. " ms")
