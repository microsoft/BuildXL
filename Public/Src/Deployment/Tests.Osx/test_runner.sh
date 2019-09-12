#!/bin/bash

readonly MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)
readonly projRootDir="$MY_DIR/TestProj"
readonly outDir="$projRootDir/Out"
readonly logsDir="$outDir/Logs"
readonly logFile="$logsDir/BuildXL.log"
readonly bxlSh="$MY_DIR/TestProj/tests/shared_bin/bxl.sh"

source "$MY_DIR/env.sh"

# set +x to relevant executables
chmod +x "$bxlSh"
find "$MY_DIR/TestProj/tests" -name "*.TestProcess" -exec chmod +x {} \;
find "$MY_DIR/TestProj/tests" -name "*CoreDump*" -exec chmod +x {} \;

# run the build

"$bxlSh"                                 \
  --config "$projRootDir/config.dsc"     \
  --symlink-sdks-into "$projRootDir/sdk" \
  --buildxl-bin "$BUILDXL_BIN"           \
  --check-kext-log 10m                   \
  /fancyConsoleMaxStatusPips:10          \
  /logsDirectory:"$logsDir"              \
  /logOutput:FullOutputOnWarningOrError  \
  /sandboxKind:macOsKext                 \
  /nowarn:0802                           \
  $@
readonly buildXLExitCode=$?

# find xunit result XML files and print out some stats
source "$MY_DIR/xunit_runner.sh"

# ensure we have enough room below
printf "\n\n${tputLineUp}${tputLineUp}"

# parse xunit result files
declare numFailed=0
readonly xunitReportFile=xunit-report.txt
> $xunitReportFile
SECONDS=0
for xunitLog in $(grep -o "Scheduled xunit test: '[^']*'" $logFile | cut -d"'" -f2 | sort); do
    echo "${tputSaveCursor}${tputBlue}[parsing]${tputReset} ${xunitLog}${tputClearLine}"
    stats=$(extract_xunit_stats "$xunitLog")
    if [[ "$?" == "0" ]]; then
        echo "${tputGreen}(passed)${tputReset} ${stats}" >> $xunitReportFile
    else
        echo "${tputRed}(failed)${tputReset} ${stats}" >> $xunitReportFile
        ((numFailed++))
    fi
    printf "${tputRestoreCursor}"
done
print_info "Done parsing xunit result files in ${SECONDS}s ${tputClearLine}"

# print report sorted by assembly name, then delete the file
cat $xunitReportFile | sort -k6 -t\|
rm $xunitReportFile

echo

if [[ $buildXLExitCode == 0 && $numFailed == 0 ]]; then
    print_info "*** All tests passed ***"
    exit 0
else
    print_error "*** Failures found: BuildXL exit code: ${buildXLExitCode}, num tests failed: ${numFailed} (scroll up for logs) ***"
    exit 1
fi