REM Produces 3 files with some content in the specified dir.
REM Parameters:
REM %1 Output directory to write outputs to.

@echo off

echo 1 > %1\1.txt
echo 3 > %1\3.txt
echo 2 > %1\2.txt