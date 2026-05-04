# Python equivalent of bench_data_transform.stash
# Builds a CSV-style dataset, parses each row, groups by department,
# computes aggregates, and serializes the result back to a key=value summary.
import time

ROWS   = 30000
PASSES = 8

def build_rows(n):
    depts = ["eng", "sales", "ops", "support", "eng", "sales"]
    names = ["Ada", "Ben", "Cyd", "Dan", "Eli", "Fay", "Gus", "Hal"]
    out = []
    for i in range(n):
        d  = depts[i % 6]
        nm = names[i % 8]
        salary = 30000 + ((i * 137) % 70000)
        out.append(str(i) + "," + nm + "," + d + "," + str(salary))
    return out

print("=== Benchmark: Data Transformation ===")

rows = build_rows(ROWS)

start = time.time()
total_summary_chars = 0
total_count = 0

for _ in range(PASSES):
    counts = {}
    sums   = {}
    maxs   = {}

    for row in rows:
        parts  = row.split(",")
        dept   = parts[2]
        salary = int(parts[3])

        counts[dept] = counts.get(dept, 0) + 1
        sums[dept]   = sums.get(dept, 0) + salary

        mx = maxs.get(dept, 0)
        if salary > mx:
            maxs[dept] = salary

        total_count += 1

    parts_out = []
    for k in list(counts.keys()):
        c   = counts.get(k, 0)
        s   = sums.get(k, 0)
        mx  = maxs.get(k, 0)
        avg = s // c
        line = (k + "=count:" + str(c)
                  + ",sum:"   + str(s)
                  + ",avg:"   + str(avg)
                  + ",max:"   + str(mx))
        parts_out.append(line)
    summary = ";".join(parts_out)
    total_summary_chars += len(summary)

elapsed = int((time.time() - start) * 1000)

print(f"Rows: {ROWS}, Passes: {PASSES}")
print(f"Total rows processed: {total_count}")
print(f"Result (checksum): {total_summary_chars}")
print(f"Time: {elapsed} ms")
