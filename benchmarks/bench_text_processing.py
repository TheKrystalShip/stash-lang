# Python equivalent of bench_text_processing.stash
# Generates synthetic log lines in memory, then filters/parses/aggregates them.
import time

LINES = 50000
SCANS = 20

def build_log(n):
    levels = ["INFO", "WARN", "ERROR", "DEBUG", "INFO", "ERROR"]
    mods   = ["auth", "db", "net", "cache", "api", "fs"]
    users  = ["alice", "bob", "carol", "dave", "eve"]
    out = []
    for i in range(n):
        lvl = levels[i % 6]
        mn  = mods[i % 6]
        u   = users[i % 5]
        ts  = "2026-01-15 12:" + str((i // 60) % 60) + ":" + str(i % 60)
        out.append(ts + " " + lvl + " [" + mn + "] event=" + str(i) + " user=" + u)
    return out

print("=== Benchmark: Text Processing ===")

lines = build_log(LINES)

start = time.time()
total_errors = 0
total_chars  = 0

for _ in range(SCANS):
    by_module = {}
    by_user   = {}
    errors    = 0

    for line in lines:
        if "ERROR" in line:
            errors += 1
            total_chars += len(line)

            lb = line.find("[")
            rb = line.find("]")
            modname = line[lb + 1:rb]

            up   = line.find("user=")
            user = line[up + 5:len(line)]

            by_module[modname] = by_module.get(modname, 0) + 1
            by_user[user]      = by_user.get(user, 0) + 1

    total_errors += errors

    for m in list(by_module.keys()):
        n  = by_module.get(m, 0)
        up = m.upper()
        rp = up.replace("_", "-")
        total_chars += len(rp) + n

elapsed = int((time.time() - start) * 1000)

print(f"Lines: {LINES}, Scans: {SCANS}")
print(f"Total errors matched: {total_errors}")
print(f"Result (checksum): {total_chars}")
print(f"Time: {elapsed} ms")
