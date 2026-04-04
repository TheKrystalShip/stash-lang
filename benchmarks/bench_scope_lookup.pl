#!/usr/bin/perl
# Perl equivalent of bench_scope_lookup.stash
use strict;
use warnings;
use Time::HiRes qw(time);

sub depth1 {
    my $a1 = 1;
    my $a2 = 2;
    my $depth2 = sub {
        my $b1 = 3;
        my $b2 = 4;
        my $depth3 = sub {
            my $c1 = 5;
            my $c2 = 6;
            my $depth4 = sub {
                my $d1 = 7;
                my $d2 = 8;
                my $depth5 = sub {
                    # Walks 4 levels up the closure chain for each variable
                    return $a1 + $a2 + $b1 + $b2 + $c1 + $c2 + $d1 + $d2;
                };
                return $depth5->();
            };
            return $depth4->();
        };
        return $depth3->();
    };
    return $depth2->();
}

my $ITERATIONS = 100000;

print "=== Benchmark: Scope Lookup ===\n";

my $start = time();
my $i = 0;
my $sum = 0;
while ($i < $ITERATIONS) {
    $sum += depth1();
    $i++;
}
my $elapsed = int((time() - $start) * 1000);

print "Iterations: $ITERATIONS\n";
print "Result (checksum): $sum\n";
print "Time: $elapsed ms\n";
