-- Lua equivalent of bench_lexer_heavy.py
-- Performs the same operations with the same iteration counts.

-- 60 variables with varied numeric literals
local v01 = 1001;     local v02 = 1002.5;   local v03 = 2003;     local v04 = 3004.25
local v05 = 5005;     local v06 = 6006.75;  local v07 = 7007;     local v08 = 8008.125
local v09 = 9009;     local v10 = 1010.5;   local v11 = 1111;     local v12 = 1212.25
local v13 = 1313;     local v14 = 1414.75;  local v15 = 1515;     local v16 = 1616.5
local v17 = 1717;     local v18 = 1818.125; local v19 = 1919;     local v20 = 2020.25
local v21 = 2121;     local v22 = 2222.5;   local v23 = 2323;     local v24 = 2424.75
local v25 = 2525;     local v26 = 2626.125; local v27 = 2727;     local v28 = 2828.25
local v29 = 2929;     local v30 = 3030.5;   local v31 = 3131;     local v32 = 3232.75
local v33 = 3333;     local v34 = 3434.125; local v35 = 3535;     local v36 = 3636.25
local v37 = 3737;     local v38 = 3838.5;   local v39 = 3939;     local v40 = 4040.75
local v41 = 4141;     local v42 = 4242.125; local v43 = 4343;     local v44 = 4444.25
local v45 = 4545;     local v46 = 4646.5;   local v47 = 4747;     local v48 = 4848.75
local v49 = 4949;     local v50 = 5050.125; local v51 = 5151;     local v52 = 5252.25
local v53 = 5353;     local v54 = 5454.5;   local v55 = 5555;     local v56 = 5656.75
local v57 = 5757;     local v58 = 5858.125; local v59 = 5959;     local v60 = 6060.25

-- String literals used in expressions
local s01 = "alpha";   local s02 = "bravo";   local s03 = "charlie"; local s04 = "delta"
local s05 = "echo";    local s06 = "foxtrot"; local s07 = "golf";    local s08 = "hotel"
local s09 = "india";   local s10 = "juliet"

local function compute(n)
    local r01 = v01 + v02 + v03 + v04 + v05 + v06 + v07 + v08 + v09 + v10
    local r02 = v11 + v12 + v13 + v14 + v15 + v16 + v17 + v18 + v19 + v20
    local r03 = v21 + v22 + v23 + v24 + v25 + v26 + v27 + v28 + v29 + v30
    local r04 = v31 + v32 + v33 + v34 + v35 + v36 + v37 + v38 + v39 + v40
    local r05 = v41 + v42 + v43 + v44 + v45 + v46 + v47 + v48 + v49 + v50
    local r06 = v51 + v52 + v53 + v54 + v55 + v56 + v57 + v58 + v59 + v60

    local tag  = s01 .. "-" .. s02 .. "-" .. tostring(n)
    local tag2 = string.upper(s03) .. "-" .. string.lower(s04)

    local mid  = (r01 + r02 + r03) - (r04 - r05 + r06)
    local deep = mid * 2 + (r01 - r02) * (r03 - r04) / 1000

    return deep + #tag + #tag2
end

local ITERATIONS = 100000

print("=== Benchmark: Lexer / Expression Throughput ===")

local start = os.clock()
local i = 0
local acc = 0.0
while i < ITERATIONS do
    acc = acc + compute(i)
    i = i + 1
end
local elapsed = math.floor((os.clock() - start) * 1000)

print("Iterations: " .. ITERATIONS)
print("Result (checksum): " .. acc)
print("Time: " .. elapsed .. " ms")
