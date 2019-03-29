#!/bin/bash

set -e

readonly statsFile1="$1"
readonly statsFile2="$2"

test -f "$statsFile1" || (echo "File '$statsFile1' not found" && exit 1)
test -f "$statsFile2" || (echo "File '$statsFile2' not found" && exit 1)

readonly diffFile="stats.diff"
readonly csvFile="stats.csv"

diff -W 530 -y <(sort "$statsFile1") <(sort "$statsFile2") | grep '|' > $diffFile
sed -e 's/[[:space:]]*|[[:space:]]*/=/g' -e 's/=/,/g' $diffFile > $csvFile

awk -F, '
$1==$3{
  printf "%-100.100s %15d %15d %15d %7d %%\n", $1, $2, $4, $4-$2, ($2==0)? 0 : ($4-$2)/$2*100
}' $csvFile
