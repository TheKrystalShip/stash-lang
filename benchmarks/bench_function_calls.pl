#!/usr/bin/perl
# Perl equivalent of bench_function_calls.stash
use strict;
use warnings;
use Time::HiRes qw(time);

sub zero_args  { return 42; }
sub one_arg    { my ($x) = @_;             return $x + 1; }
sub two_args   { my ($a, $b) = @_;         return $a + $b; }
sub three_args { my ($a, $b, $c) = @_;     return $a + $b + $c; }
sub four_args  { my ($a, $b, $c, $d) = @_; return $a + $b + $c + $d; }

sub compute {
    my ($x) = @_;
    my $t = $x * 3;
    my $u = $t - $x;
    return $u + 1;
}

my $ITERATIONS = 100000;
print "=== Benchmark: Function Calls ===\n";

my $start = time();

my $i = 0;
my $acc = 0;
while ($i < $ITERATIONS) {
    $acc += zero_args();
    $acc += one_arg($i);
    $acc += two_args($i, 2);
    $acc += three_args($i, 2, 3);
    $acc += four_args($i, 1, 2, 3);
    $acc += compute($i);
    $i++;
}

my $elapsed = int((time() - $start) * 1000);
print "Iterations: $ITERATIONS\n";
print "Result (checksum): $acc\n";
print "Time: $elapsed ms\n";
