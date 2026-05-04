#!/usr/bin/perl
# Perl equivalent of bench_data_transform.stash
use strict;
use warnings;
use Time::HiRes qw(time);

my $ROWS   = 30000;
my $PASSES = 8;

sub build_rows {
    my ($n) = @_;
    my @depts = ("eng", "sales", "ops", "support", "eng", "sales");
    my @names = ("Ada", "Ben", "Cyd", "Dan", "Eli", "Fay", "Gus", "Hal");
    my @out;
    for (my $i = 0; $i < $n; $i++) {
        my $d  = $depts[$i % 6];
        my $nm = $names[$i % 8];
        my $salary = 30000 + (($i * 137) % 70000);
        push @out, $i . "," . $nm . "," . $d . "," . $salary;
    }
    return \@out;
}

print "=== Benchmark: Data Transformation ===\n";

my $rows = build_rows($ROWS);

my $start = time();
my $total_summary_chars = 0;
my $total_count = 0;

for (my $p = 0; $p < $PASSES; $p++) {
    my %counts;
    my %sums;
    my %maxs;

    for my $row (@$rows) {
        my @parts  = split(/,/, $row);
        my $dept   = $parts[2];
        my $salary = $parts[3] + 0;

        $counts{$dept} = ($counts{$dept} // 0) + 1;
        $sums{$dept}   = ($sums{$dept} // 0) + $salary;

        my $mx = $maxs{$dept} // 0;
        if ($salary > $mx) {
            $maxs{$dept} = $salary;
        }

        $total_count++;
    }

    my @parts_out;
    for my $k (keys %counts) {
        my $c   = $counts{$k} // 0;
        my $s   = $sums{$k} // 0;
        my $mx  = $maxs{$k} // 0;
        my $avg = int($s / $c);
        my $line = $k . "=count:" . $c
                      . ",sum:"   . $s
                      . ",avg:"   . $avg
                      . ",max:"   . $mx;
        push @parts_out, $line;
    }
    my $summary = join(";", @parts_out);
    $total_summary_chars += length($summary);
}

my $elapsed = int((time() - $start) * 1000);

print "Rows: $ROWS, Passes: $PASSES\n";
print "Total rows processed: $total_count\n";
print "Result (checksum): $total_summary_chars\n";
print "Time: $elapsed ms\n";
