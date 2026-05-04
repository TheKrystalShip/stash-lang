-- Lua equivalent of bench_data_transform.stash
-- Builds a CSV-style dataset, parses each row, groups by department,
-- computes aggregates, and serializes the result back to a key=value summary.

local ROWS   = 30000
local PASSES = 8

local function build_rows(n)
    local depts = {"eng", "sales", "ops", "support", "eng", "sales"}
    local names = {"Ada", "Ben", "Cyd", "Dan", "Eli", "Fay", "Gus", "Hal"}
    local out = {}
    for i = 0, n - 1 do
        local d  = depts[(i % 6) + 1]
        local nm = names[(i % 8) + 1]
        local salary = 30000 + ((i * 137) % 70000)
        out[#out + 1] = tostring(i) .. "," .. nm .. "," .. d .. "," .. tostring(salary)
    end
    return out
end

print("=== Benchmark: Data Transformation ===")

local rows = build_rows(ROWS)

local start = os.clock()
local total_summary_chars = 0
local total_count = 0

for p = 1, PASSES do
    local counts = {}
    local sums   = {}
    local maxs   = {}

    for i = 1, #rows do
        local row = rows[i]
        local parts = {}
        local idx = 1
        for field in string.gmatch(row, "([^,]+)") do
            parts[idx] = field
            idx = idx + 1
        end
        local dept   = parts[3]
        local salary = tonumber(parts[4])

        counts[dept] = (counts[dept] or 0) + 1
        sums[dept]   = (sums[dept] or 0) + salary

        local mx = maxs[dept] or 0
        if salary > mx then
            maxs[dept] = salary
        end

        total_count = total_count + 1
    end

    local parts_out = {}
    for k, _ in pairs(counts) do
        local c   = counts[k] or 0
        local s   = sums[k] or 0
        local mx  = maxs[k] or 0
        local avg = math.floor(s / c)
        local line = k .. "=count:" .. tostring(c)
                       .. ",sum:"   .. tostring(s)
                       .. ",avg:"   .. tostring(avg)
                       .. ",max:"   .. tostring(mx)
        parts_out[#parts_out + 1] = line
    end
    local summary = table.concat(parts_out, ";")
    total_summary_chars = total_summary_chars + #summary
end

local elapsed = math.floor((os.clock() - start) * 1000)

print("Rows: " .. ROWS .. ", Passes: " .. PASSES)
print("Total rows processed: " .. total_count)
print("Result (checksum): " .. total_summary_chars)
print("Time: " .. elapsed .. " ms")
