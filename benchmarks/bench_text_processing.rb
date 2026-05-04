# Ruby equivalent of bench_text_processing.stash
# Generates synthetic log lines in memory, then filters/parses/aggregates them.

LINES = 50000
SCANS = 20

def build_log(n)
  levels = ["INFO", "WARN", "ERROR", "DEBUG", "INFO", "ERROR"]
  mods   = ["auth", "db", "net", "cache", "api", "fs"]
  users  = ["alice", "bob", "carol", "dave", "eve"]
  out = []
  i = 0
  while i < n
    lvl = levels[i % 6]
    mn  = mods[i % 6]
    u   = users[i % 5]
    ts  = "2026-01-15 12:" + ((i / 60) % 60).to_s + ":" + (i % 60).to_s
    out << (ts + " " + lvl + " [" + mn + "] event=" + i.to_s + " user=" + u)
    i += 1
  end
  out
end

puts "=== Benchmark: Text Processing ==="

lines = build_log(LINES)

start = Process.clock_gettime(Process::CLOCK_MONOTONIC)
total_errors = 0
total_chars  = 0

SCANS.times do
  by_module = {}
  by_user   = {}
  errors = 0

  lines.each do |line|
    if line.include?("ERROR")
      errors += 1
      total_chars += line.length

      lb = line.index("[")
      rb = line.index("]")
      modname = line[lb + 1...rb]

      up   = line.index("user=")
      user = line[up + 5...line.length]

      by_module[modname] = (by_module[modname] || 0) + 1
      by_user[user]      = (by_user[user] || 0) + 1
    end
  end

  total_errors += errors

  by_module.keys.each do |m|
    n  = by_module[m] || 0
    up = m.upcase
    rp = up.gsub("_", "-")
    total_chars += rp.length + n
  end
end

elapsed = ((Process.clock_gettime(Process::CLOCK_MONOTONIC) - start) * 1000).to_i

puts "Lines: #{LINES}, Scans: #{SCANS}"
puts "Total errors matched: #{total_errors}"
puts "Result (checksum): #{total_chars}"
puts "Time: #{elapsed} ms"
