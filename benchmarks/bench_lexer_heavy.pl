#!/usr/bin/perl
# Perl equivalent of bench_lexer_heavy.stash
use strict;
use warnings;
use Time::HiRes qw(time);

# 60 variables with varied numeric literals — stresses lexer tokenisation
my $v01 = 1001;     my $v02 = 1002.5;   my $v03 = 2003;     my $v04 = 3004.25;
my $v05 = 5005;     my $v06 = 6006.75;  my $v07 = 7007;     my $v08 = 8008.125;
my $v09 = 9009;     my $v10 = 1010.5;   my $v11 = 1111;     my $v12 = 1212.25;
my $v13 = 1313;     my $v14 = 1414.75;  my $v15 = 1515;     my $v16 = 1616.5;
my $v17 = 1717;     my $v18 = 1818.125; my $v19 = 1919;     my $v20 = 2020.25;
my $v21 = 2121;     my $v22 = 2222.5;   my $v23 = 2323;     my $v24 = 2424.75;
my $v25 = 2525;     my $v26 = 2626.125; my $v27 = 2727;     my $v28 = 2828.25;
my $v29 = 2929;     my $v30 = 3030.5;   my $v31 = 3131;     my $v32 = 3232.75;
my $v33 = 3333;     my $v34 = 3434.125; my $v35 = 3535;     my $v36 = 3636.25;
my $v37 = 3737;     my $v38 = 3838.5;   my $v39 = 3939;     my $v40 = 4040.75;
my $v41 = 4141;     my $v42 = 4242.125; my $v43 = 4343;     my $v44 = 4444.25;
my $v45 = 4545;     my $v46 = 4646.5;   my $v47 = 4747;     my $v48 = 4848.75;
my $v49 = 4949;     my $v50 = 5050.125; my $v51 = 5151;     my $v52 = 5252.25;
my $v53 = 5353;     my $v54 = 5454.5;   my $v55 = 5555;     my $v56 = 5656.75;
my $v57 = 5757;     my $v58 = 5858.125; my $v59 = 5959;     my $v60 = 6060.25;

# String literals used in expressions — stresses string token scanning
my $s01 = "alpha";   my $s02 = "bravo";   my $s03 = "charlie"; my $s04 = "delta";
my $s05 = "echo";    my $s06 = "foxtrot"; my $s07 = "golf";    my $s08 = "hotel";
my $s09 = "india";   my $s10 = "juliet";

# Compute function: deeply nested arithmetic over many identifiers
sub compute {
    my ($n) = @_;
    my $r01 = $v01 + $v02 + $v03 + $v04 + $v05 + $v06 + $v07 + $v08 + $v09 + $v10;
    my $r02 = $v11 + $v12 + $v13 + $v14 + $v15 + $v16 + $v17 + $v18 + $v19 + $v20;
    my $r03 = $v21 + $v22 + $v23 + $v24 + $v25 + $v26 + $v27 + $v28 + $v29 + $v30;
    my $r04 = $v31 + $v32 + $v33 + $v34 + $v35 + $v36 + $v37 + $v38 + $v39 + $v40;
    my $r05 = $v41 + $v42 + $v43 + $v44 + $v45 + $v46 + $v47 + $v48 + $v49 + $v50;
    my $r06 = $v51 + $v52 + $v53 + $v54 + $v55 + $v56 + $v57 + $v58 + $v59 + $v60;

    my $tag  = "$s01-$s02-$n";
    my $tag2 = uc($s03) . "-" . lc($s04);

    my $mid  = ($r01 + $r02 + $r03) - ($r04 - $r05 + $r06);
    my $deep = $mid * 2 + ($r01 - $r02) * ($r03 - $r04) / 1000;

    return $deep + length($tag) + length($tag2);
}

my $ITERATIONS = 100000;

print "=== Benchmark: Lexer / Expression Throughput ===\n";

my $start = time();
my $i = 0;
my $acc = 0.0;
while ($i < $ITERATIONS) {
    $acc += compute($i);
    $i++;
}
my $elapsed = int((time() - $start) * 1000);

print "Iterations: $ITERATIONS\n";
print "Result (checksum): $acc\n";
print "Time: $elapsed ms\n";
