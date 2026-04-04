#!/usr/bin/perl
# Perl equivalent of bench_namespace_calls.stash
use strict;
use warnings;
use Time::HiRes qw(time);
use POSIX qw(floor);

my $ITERATIONS = 200000;

print "=== Benchmark: Namespace Calls ===\n";

my $start = time();
my $i = 0;
my $acc = 0.0;
while ($i < $ITERATIONS) {
    # math namespace
    my $sq = sqrt($i + 1);
    my $ab = abs(-$i - 1);
    my $fl = floor($sq + 0.5);
    my $pw = 2 ** 8;
    my $mx = $i > $i + 1 ? $i : $i + 1;
    my $mn = $i < $i + 1 ? $i : $i + 1;

    # str namespace
    my $up = uc("hello");
    my $lo = lc("WORLD");
    my $tr = "  stash  " =~ s/^\s+|\s+$//gr;
    my $rp = "abc" =~ s/b/B/gr;

    # conv namespace
    my $cs  = "$i";
    my $ci  = int("42");
    my $chx = sprintf("%x", 255);

    $acc = $acc + $sq + $ab + $fl + $pw + $mx + $mn;
    $i++;
}
my $elapsed = int((time() - $start) * 1000);

print "Iterations: $ITERATIONS\n";
print "Result (checksum): $acc\n";
print "Time: $elapsed ms\n";
