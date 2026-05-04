-- Lua equivalent of bench_text_processing.stash
-- Generates synthetic log lines in memory, then filters/parses/aggregates them.

local LINES = 50000
local SCANS = 20

local function build_log(n)
    local levels = {"INFO", "WARN", "ERROR", "DEBUG", "INFO", "ERROR"}
    local mods   = {"auth", "db", "net", "cache", "api", "fs"}
    local users  = {"alice", "bob", "carol", "dave", "eve"}
    local out = {}
    for i = 0, n - 1 do
        local lvl = levels[(i % 6) + 1]
        local mn  = mods[(i % 6) + 1]
        local u   = users[(i % 5) + 1]
        local ts  = "2026-01-15 12:" .. tostring(math.floor(i / 60) % 60) .. ":" .. tostring(i % 60)
        out[#out + 1] = ts .. " " .. lvl .. " [" .. mn .. "] event=" .. tostring(i) .. " user=" .. u
    end
    return out
end

print("=== Benchmark: Text Processing ===")

local lines = build_log(LINES)

local start = os.clock()
local total_errors = 0
local total_chars  = 0

for s = 1, SCANS do
    local by_module = {}
    local by_user   = {}
    local errors = 0

    for i = 1, #lines do
        local line = lines[i]
        if string.find(line, "ERROR", 1, true) then
            errors = errors + 1
            total_chars = total_chars + #line

            local lb = string.find(line, "[", 1, true)
            local rb = string.find(line, "]", 1, true)
            local modname = string.sub(line, lb + 1, rb - 1)

            local up = string.find(line, "user=", 1, true)
            local user = string.sub(line, up + 5)

            by_module[modname] = (by_module[modname] or 0) + 1
            by_user[user]      = (by_user[user] or 0) + 1
        end
    end

    total_errors = total_errors + errors

    for m, _ in pairs(by_module) do
        local n  = by_module[m] or 0
        local up = string.upper(m)
        local rp = string.gsub(up, "_", "-")
        total_chars = total_chars + #rp + n
    end
end

local elapsed = math.floor((os.clock() - start) * 1000)

print("Lines: " .. LINES .. ", Scans: " .. SCANS)
print("Total errors matched: " .. total_errors)
print("Result (checksum): " .. total_chars)
print("Time: " .. elapsed .. " ms")
