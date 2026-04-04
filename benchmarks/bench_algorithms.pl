#!/usr/bin/perl
# Perl equivalent of bench_algorithms.stash
use strict;
use warnings;
use Time::HiRes qw(time);

# --- Recursive Fibonacci ---

sub fibonacci {
    my ($n) = @_;
    if ($n <= 0) { return 0; }
    if ($n == 1) { return 1; }
    return fibonacci($n - 1) + fibonacci($n - 2);
}

# --- Bubble Sort ---

sub bubble_sort {
    my ($arr) = @_;
    my $n = scalar @$arr;
    my $swapped = 1;
    while ($swapped) {
        $swapped = 0;
        my $j = 0;
        while ($j < $n - 1) {
            if ($arr->[$j] > $arr->[$j + 1]) {
                my $tmp = $arr->[$j];
                $arr->[$j] = $arr->[$j + 1];
                $arr->[$j + 1] = $tmp;
                $swapped = 1;
            }
            $j++;
        }
        $n--;
    }
    return $arr;
}

# --- Binary Search (requires sorted array) ---

sub binary_search {
    my ($arr, $target) = @_;
    my $lo = 0;
    my $hi = scalar(@$arr) - 1;
    while ($lo <= $hi) {
        my $mid = int(($lo + $hi) / 2);
        if ($arr->[$mid] == $target) { return $mid; }
        if ($arr->[$mid] < $target) { $lo = $mid + 1; }
        else                        { $hi = $mid - 1; }
    }
    return -1;
}

# --- Node hash build + sum ---

sub build_nodes {
    my ($count) = @_;
    my @nodes;
    my $i = 0;
    while ($i < $count) {
        push @nodes, {value => $i * 3 + 1, index => $i};
        $i++;
    }
    return \@nodes;
}

sub sum_nodes {
    my ($nodes) = @_;
    my $total = 0;
    for my $n (@$nodes) {
        $total += $n->{value};
    }
    return $total;
}

# =============================================================================
# Run benchmarks
# =============================================================================

print "=== Benchmark: Algorithms ===\n";
print "\n";

# 1. Fibonacci(26) — deep recursion tree

my $t0 = time();
my $fib_result = fibonacci(26);
my $fib_time = int((time() - $t0) * 1000);
print "fibonacci(26)      = $fib_result\n";
print "Time: $fib_time ms\n";
print "\n";

# 2. Bubble sort on 1000-element descending array (worst case)

my @sort_data;
my $si = 1000;
while ($si > 0) {
    push @sort_data, $si;
    $si--;
}

my $t1 = time();
my $sorted = bubble_sort(\@sort_data);
my $sort_time = int((time() - $t1) * 1000);
print "bubble_sort(1000)  first=$sorted->[0], last=$sorted->[-1]\n";
print "Time: $sort_time ms\n";
print "\n";

# 3. Binary search — 10,000 searches on the sorted array

my $t2 = time();
my $found_count = 0;
my $bi = 0;
while ($bi < 10000) {
    my $target = ($bi % 1000) + 1;
    my $idx = binary_search($sorted, $target);
    if ($idx >= 0) { $found_count++; }
    $bi++;
}
my $search_time = int((time() - $t2) * 1000);
print "binary_search x10000  found=$found_count\n";
print "Time: $search_time ms\n";
print "\n";

# 4. Node build + aggregate — 5000 node hashes

my $t3 = time();
my $nodes = build_nodes(5000);
my $node_sum = sum_nodes($nodes);
my $struct_time = int((time() - $t3) * 1000);
print "build+sum nodes(5000) sum=$node_sum\n";
print "Time: $struct_time ms\n";
print "\n";

my $total_time = $fib_time + $sort_time + $search_time + $struct_time;
print "Total time: $total_time ms\n";
