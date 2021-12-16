#!/bin/bash

readonly MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)
readonly ARGS="$@"

function run_build {
    /bin/bash $MY_DIR/build.sh /logsDirectory:"$bxlLogDir" /o:"$bxlObjDir" $ARGS "$@"
}

function check_fully_cached {
    print_info "Asserting build was fully cached..."
    grep -q "Process pip cache hits: 100%" $bxlLogFile
    check_success $?
}

function check_success {
    if [[ $@ != 0 ]]; then
        print_error "Assertion failed."
        exit 2
    fi
}

function check_observed_writes {
    print_info "Asserting build produced observable outputs..."
    grep -q 'MAC_VNODE_CREATE\|VNODE_WRITE' $bxlLogFile
    check_success $?
}

function print_header {
    echo " ==============================================================================="
    echo " === $@"
    echo " ==============================================================================="
}

# ======================= script entry point

source "$MY_DIR/env.sh"

readonly bxlOutDir="$MY_DIR/out"
readonly bxlObjDir="$bxlOutDir/objects.noindex"
readonly bxlLogDir="$bxlOutDir/logs"
readonly bxlLogFile="$bxlLogDir/BuildXL.log"

# start with clean Out dir, presrving log files
find $bxlOutDir/ ! -name "*1st.log" ! -name "*2nd.log" -type f -delete

# 1st run
print_header "1st run: clean build"
run_build
check_success $?
check_observed_writes

mv $bxlLogDir/BuildXL.log $bxlLogDir/BuildXL-$((1 + $RANDOM % 50000))-1st.log

# 2nd run: clean Obj dir, and run build again
echo
print_info "Deleting $bxlObjDir"
rm -rf "$bxlObjDir"*
print_header "2nd run: fully cached"
run_build
check_success $?
check_fully_cached
mv $bxlLogDir/BuildXL.log $bxlLogDir/BuildXL-$((1 + $RANDOM % 50000))-2nd.log