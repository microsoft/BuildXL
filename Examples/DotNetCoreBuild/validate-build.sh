#!/bin/bash

readonly MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)
readonly ARGS="$@"

function run_build {
    /bin/bash $MY_DIR/validate-build-kext.sh /logsDirectory:"$bxlLogDir" /o:"$bxlObjDir" $ARGS "$@"
}

function check_graph_reloaded {
    grep -q "Reloading pip graph from previous build." $bxlLogFile
}

function check_fully_cached {
    grep -q "Process pip cache hits: 100%" $bxlLogFile
}

readonly GRAPH_RELOADED=0
readonly GRAPH_NOT_RELOADED=1
readonly FULLY_CACHED=0
readonly NOT_FULLY_CACHED=1

# this is the magic timestamp ("2003-03-03 3:03:03") translated to UTC Epoch seconds
# readonly magicTimestamp=$(date -j -u -f "%Y-%m-%d %H:%M:%S" "2003-03-03 3:03:03" +%s)
readonly magicXattrName="com.microsoft.buildxl:shared_opaque_output"

function run_build_and_check_stuff {
    local expectGraphReloadedStatus=$1
    shift
    local expectFullyCachedStatus=$1
    shift
    local rest="$@"

    # run Bxl build
    run_build $rest
    local bxlExit=$?
    if [[ $bxlExit != 0 ]]; then
        print_error "Bxl failed with code $bxlExit"
        return 1
    fi

    # check graph reloaded
    check_graph_reloaded
    local status=$?
    if [[ $status != $expectGraphReloadedStatus ]]; then
        print_error "Graph validation failed :: status = $status, expected = $expectGraphReloadedStatus ($GRAPH_RELOADED = graph reloaded, $GRAPH_NOT_RELOADED = graph not reloaded)"
        return 2
    else
        print_info $(if [[ $expectGraphReloadedStatus == $GRAPH_RELOADED ]]; then echo "Verified graph reloaded"; else echo "Verified graph NOT reloaded"; fi)
    fi

    # check fully cached
    check_fully_cached
    status=$?
    if [[ $status != $expectFullyCachedStatus ]]; then
        print_error "Cache validation failed :: status = $status, expected = $expectFullyCachedStatus ($FULLY_CACHED = fully cached, $NOT_FULLY_CACHED = not fully cached)"
        return 3
    else
        print_info $(if [[ $expectFullyCachedStatus == $FULLY_CACHED ]]; then echo "Verified build fully cached"; else echo "Verified build NOT fully cached"; fi)
    fi

    # check that all files in all shared opaque directories (whose name is 'sod*') have the magic xattr
    local sodFilesFile="$MY_DIR/sod-files.txt"
    find "$MY_DIR/out/objects" -type d -name 'sod*' -exec find {} -type f -o -type l \; > "$sodFilesFile"
    for f in `cat $sodFilesFile`; do
        xattr -s "$f" | grep -q $magicXattrName || {
            print_error "File '$f' does not have the magic xattr '$magicXattrName'"
            return 4
        }
    done
    rm -f "$sodFilesFile"
}

function print_header {
    echo " ==============================================================================="
    echo " === $@"
    echo " ==============================================================================="
}

# ======================= script entry point

source "$MY_DIR/env.sh"

readonly bxlOutDir="$MY_DIR/out"
readonly bxlObjDir="$bxlOutDir/objects"
readonly bxlLogDir="$bxlOutDir/logs"
readonly bxlLogFile="$bxlLogDir/BuildXL.log"

readonly unusedFilePath=$(find $MY_DIR/test -name "unused-file.txt")
if [[ ! -f $unusedFilePath ]]; then
    print_error "Cannot find 'unused-file.txt'; goal: modify it, run a second build, and assert the second build still gets 100% cache hit rate"
    exit 1
fi

# start with clean Out dir
rm -rf "$bxlOutDir"

# 1st run
print_header "1st run: clean build"
run_build_and_check_stuff $GRAPH_NOT_RELOADED $NOT_FULLY_CACHED
if [[ $? != 0 ]]; then
    print_error "1st build failed"
    exit 1
fi

# Check that scrubbing deletes symlinks within shared opaque directories
print_header "Run with /phase:Schedule and check that symlinks are deleted"
run_build /phase:Schedule
declare producedSymlinkFileName="module.config.dsc"
declare scrubbedSymlinkFile=$(grep -o "Scrubber deletes file '.*/$producedSymlinkFileName'" $bxlLogFile | grep -o "'.*'")
if [[ -z $scrubbedSymlinkFile ]]; then
    print_error "Expected produced symlink to $producedSymlinkFileName to have been deleted"
    exit 1
fi

print_info "Symlink was scrubbed: $scrubbedSymlinkFile"

if [[ -f $scrubbedSymlinkFile ]]; then
    print_error "File '$scrubbedSymlinkFile' exists on disk"
    exit 1
fi

declare foundSymlinkFiles=$(find $bxlOutDir -type f -name $producedSymlinkFileName)
if [[ -n $foundSymlinkFiles ]]; then
    print_error "File '$producedSymlinkFileName' found in object folder:"
    echo $foundSymlinkFiles
    exit 1
fi

# 2nd run: clean Obj dir, modify "unused file", and run build again
echo
print_info "Deleting $bxlObjDir"
rm -rf "$bxlObjDir"*
print_info "$(mv -v $unusedFilePath $unusedFilePath.bak)"
print_header "2nd run: fully cached but no graph reloaded (because content of a globbed folder changed)."
run_build_and_check_stuff $GRAPH_NOT_RELOADED $FULLY_CACHED
declare status=$?

# revert "unused" file
print_info "$(mv -v $unusedFilePath.bak $unusedFilePath)"

# check 2nd build succeeded
if [[ $status != 0 ]]; then
    print_error "2nd build failed"
    exit 2
fi

# 3nd run
echo
print_header "3rd run: fully cached and graph reloaded (because nothing changed since last run)."
run_build_and_check_stuff $GRAPH_RELOADED $FULLY_CACHED
if [[ $status != 0 ]]; then
    print_error "3nd build failed"
    exit 2
fi

echo "${tputBold}${tputGreen}Build Validation Succeeded${tputReset}"