REM Produces 5 files with some content, the first 3 are in the directory specified in the first argument, 
REM and the last two are specified explicitly in the second and third arguments.
REM Parameters:
REM %1 Output directory to write outputs to.
REM %2 An output file.
REM %3 An output file.

@echo off

echo 1 > %1\1.txt
mkdir %1\A
echo 2 > %1\A\2.txt
mkdir %1\A\B
echo 3 > %1\A\B\3.txt

echo out2 > %2
echo out3 > %3