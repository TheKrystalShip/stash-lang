# Python equivalent of bench_function_calls.stash
# Performs the same operations with the same iteration counts.
import time


def zero_args():
    return 42


def one_arg(x):
    return x + 1


def two_args(a, b):
    return a + b


def three_args(a, b, c):
    return a + b + c


def four_args(a, b, c, d):
    return a + b + c + d


def compute(x):
    t = x * 3
    u = t - x
    return u + 1


ITERATIONS = 100000
print("=== Benchmark: Function Calls ===")

start = time.time()

i = 0
acc = 0

while i < ITERATIONS:
    acc = acc + zero_args()
    acc = acc + one_arg(i)
    acc = acc + two_args(i, 2)
    acc = acc + three_args(i, 2, 3)
    acc = acc + four_args(i, 1, 2, 3)
    acc = acc + compute(i)
    i += 1

elapsed = int((time.time() - start) * 1000)
print(f"Iterations: {ITERATIONS}")
print(f"Result (checksum): {acc}")
print(f"Time: {elapsed} ms")
