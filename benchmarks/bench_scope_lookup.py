# Python equivalent of bench_scope_lookup.stash
# Performs the same operations with the same iteration counts.
import time


def depth1():
    a1 = 1
    a2 = 2
    def depth2():
        b1 = 3
        b2 = 4
        def depth3():
            c1 = 5
            c2 = 6
            def depth4():
                d1 = 7
                d2 = 8
                def depth5():
                    # Walks 4 levels up the closure chain for each variable
                    return a1 + a2 + b1 + b2 + c1 + c2 + d1 + d2
                return depth5()
            return depth4()
        return depth3()
    return depth2()


ITERATIONS = 100000

print("=== Benchmark: Scope Lookup ===")

start = time.time()
i = 0
total = 0
while i < ITERATIONS:
    total = total + depth1()
    i += 1
elapsed = int((time.time() - start) * 1000)

print(f"Iterations: {ITERATIONS}")
print(f"Result (checksum): {total}")
print(f"Time: {elapsed} ms")
