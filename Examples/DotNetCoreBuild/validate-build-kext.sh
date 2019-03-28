#!/bin/bash

readonly MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)
readonly HELLO_WORLD_MSG="HelloWorldFromBuildXLOnMac"

readonly bxlOutDir="$MY_DIR/out"
readonly bxlObjDir="$bxlOutDir/objects"
readonly bxlLogDir="$bxlOutDir/logs"
readonly bxlLogFile="$bxlLogDir/BuildXL.log"

source "$MY_DIR/env.sh"

# check if SIP is disabled
readonly sipStatus="$(csrutil status)"
print_info "$sipStatus"
if [[ -z $(echo "$sipStatus" | grep -o "disabled") ]]; then
    print_warning "SIP is not disabled"
fi

# check Kext is running
declare additionalBuildArgs=""
print_info "Checking if Bxl Kernel Extension (Kext) is loaded"
readonly bxlKext=$(kextstat | grep com.microsoft.buildxl)
if [[ -z $bxlKext ]]; then
    print_warning "Kext is not running, adding '--load-kext' build arguments"
    additionalBuildArgs="--load-kext"
fi

print_info $(ln -sfv src-file.txt "$MY_DIR/test/TestSymlink/symlink-to-src-file.txt")

# run Bxl
chmod +x "$MY_DIR/build.sh"
"$MY_DIR/build.sh"                               \
  /logsDirectory:"$bxlLogDir"                    \
  /o:"$bxlObjDir"                                \
  /p:"HELLO_WORLD_MSG=$HELLO_WORLD_MSG"          \
  /nowarn:0802 $bxlExtraArgs                     \
  /sandboxKind:macOsKext                         \
  /kextThrottleCpuUsageBlockThresholdPercent:55  \
  /kextThrottleCpuUsageWakeupThresholdPercent:50 \
  /kextThrottleMinAvailableRamMB:1024            \
  ${additionalBuildArgs}                         \
  "$@"
bxlExitCode=$?

# check Bxl succeeded
if [[ $bxlExitCode != 0 ]]; then
    exit $bxlExitCode
fi

# check that Bxl.log file exists
print_info "Checking existence of Bxl log file"
echo "  found: $bxlLogFile"
if [[ ! -f $bxlLogFile ]]; then
    print_error "Bxl log file ($bxlLogFile) not found"
    exit 1
fi

echo "${tputBold}${tputGreen}Build Validation Succeeded${tputReset}"

