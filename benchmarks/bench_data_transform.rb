# Ruby equivalent of bench_data_transform.stash
# Builds a CSV-style dataset, parses each row, groups by department,
# computes aggregates, and serializes the result back to a key=value summary.

ROWS   = 30000
PASSES = 8

def build_rows(n)
  depts = ["eng", "sales", "ops", "support", "eng", "sales"]
  names = ["Ada", "Ben", "Cyd", "Dan", "Eli", "Fay", "Gus", "Hal"]
  out = []
  i = 0
  while i < n
    d  = depts[i % 6]
    nm = names[i % 8]
    salary = 30000 + ((i * 137) % 70000)
    out << (i.to_s + "," + nm + "," + d + "," + salary.to_s)
    i += 1
  end
  out
end

puts "=== Benchmark: Data Transformation ==="

rows = build_rows(ROWS)

start = Process.clock_gettime(Process::CLOCK_MONOTONIC)
total_summary_chars = 0
total_count = 0

PASSES.times do
  counts = {}
  sums   = {}
  maxs   = {}

  rows.each do |row|
    parts  = row.split(",")
    dept   = parts[2]
    salary = parts[3].to_i

    counts[dept] = (counts[dept] || 0) + 1
    sums[dept]   = (sums[dept] || 0) + salary

    mx = maxs[dept] || 0
    if salary > mx
      maxs[dept] = salary
    end

    total_count += 1
  end

  parts_out = []
  counts.keys.each do |k|
    c   = counts[k] || 0
    s   = sums[k] || 0
    mx  = maxs[k] || 0
    avg = s / c
    line = k + "=count:" + c.to_s +
               ",sum:"   + s.to_s +
               ",avg:"   + avg.to_s +
               ",max:"   + mx.to_s
    parts_out << line
  end
  summary = parts_out.join(";")
  total_summary_chars += summary.length
end

elapsed = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - start) * 1000).to_i

puts "Rows: #{ROWS}, Passes: #{PASSES}"
puts "Total rows processed: #{total_count}"
puts "Result (checksum): #{total_summary_chars}"
puts "Time: #{elapsed} ms"
