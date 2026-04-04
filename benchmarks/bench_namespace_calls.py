# Python equivalent of bench_namespace_calls.stash
# Performs the same operations with the same iteration counts.
import time
import math

ITERATIONS = 200000

print("=== Benchmark: Namespace Calls ===")

start = time.time()
i = 0
acc = 0.0
while i < ITERATIONS:
    # math namespace equivalents
    sq  = math.sqrt(i + 1)
    ab  = math.fabs(-i - 1)
    fl  = math.floor(sq + 0.5)
    pw  = math.pow(2, 8)
    mx  = max(i, i + 1)
    mn  = min(i, i + 1)

    # str namespace equivalents
    up  = "hello".upper()
    lo  = "WORLD".lower()
    tr  = "  stash  ".strip()
    rp  = "abc".replace("b", "B")

    # conv namespace equivalents
    cs  = str(i)
    ci  = int("42")
    chx = hex(255)

    acc = acc + sq + ab + fl + pw + mx + mn
    i += 1
elapsed = int((time.time() - start) * 1000)

print(f"Iterations: {ITERATIONS}")
print(f"Result (checksum): {acc}")
print(f"Time: {elapsed} ms")
