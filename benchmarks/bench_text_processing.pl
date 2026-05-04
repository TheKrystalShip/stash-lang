#!/usr/bin/perl
# Perl equivalent of bench_text_processing.stash
use strict;
use warnings;
use Time::HiRes qw(time);

my $LINES = 50000;
my $SCANS = 20;

sub build_log {
    my ($n) = @_;
    my @levels = ("INFO", "WARN", "ERROR", "DEBUG", "INFO", "ERROR");
    my @mods   = ("auth", "db", "net", "cache", "api", "fs");
    my @users  = ("alice", "bob", "carol", "dave", "eve");
    my @out;
    for (my $i = 0; $i < $n; $i++) {
        my $lvl = $levels[$i % 6];
        my $mn  = $mods[$i % 6];
        my $u   = $users[$i % 5];
        my $ts  = "2026-01-15 12:" . (int($i / 60) % 60) . ":" . ($i % 60);
        push @out, $ts . " " . $lvl . " [" . $mn . "] event=" . $i . " user=" . $u;
    }
    return \@out;
}

print "=== Benchmark: Text Processing ===\n";

my $lines = build_log($LINES);

my $start = time();
my $total_errors = 0;
my $total_chars  = 0;

for (my $s = 0; $s < $SCANS; $s++) {
    my %by_module;
    my %by_user;
    my $errors = 0;

    for my $line (@$lines) {
        if (index($line, "ERROR") != -1) {
            $errors++;
            $total_chars += length($line);

            my $lb = index($line, "[");
            my $rb = index($line, "]");
            my $modname = substr($line, $lb + 1, $rb - $lb - 1);

            my $up   = index($line, "user=");
            my $user = substr($line, $up + 5);

            $by_module{$modname} = ($by_module{$modname} // 0) + 1;
            $by_user{$user}      = ($by_user{$user} // 0) + 1;
        }
    }

    $total_errors += $errors;

    for my $m (keys %by_module) {
        my $n  = $by_module{$m} // 0;
        my $up = uc $m;
        (my $rp = $up) =~ s/_/-/g;
        $total_chars += length($rp) + $n;
    }
}

my $elapsed = int((time() - $start) * 1000);

print "Lines: $LINES, Scans: $SCANS\n";
print "Total errors matched: $total_errors\n";
print "Result (checksum): $total_chars\n";
print "Time: $elapsed ms\n";
