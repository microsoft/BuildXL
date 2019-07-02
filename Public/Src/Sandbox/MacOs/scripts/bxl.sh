#!/bin/bash

set -e

MY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

source "$MY_DIR/env.sh"

declare ERROR_BUILDXL_BIN_NOT_DEFINED=-1
declare ERROR_BUILDXL_BIN_NOT_FOLDER=-2
declare ERROR_BUILDXL_FILE_NOT_PRESENT=-3
declare ERROR_CANNOT_SYMLINK_SDK=-4
declare ERROR_CONFIG_NOT_SPECIFIED=-5
declare DEFAULT_CACHE_CONFIG_FILE_NAME=DefaultCacheConfig.json

declare arg_CacheConfigFile=""
declare arg_Positional=()
declare arg_BuildXLBin=""
declare arg_MainConfig=""
declare arg_SymlinkSdksInto=""
declare arg_checkKextLogInterval=""
declare arg_loadKext=""

declare g_bxlCmdArgs=()

# Allow for up to 1MB of thread stack size
export COMPlus_DefaultStackSize=100000

# Clears and then populates the 'g_bxlArgs' array with arguments to be passed to 'bxl'.
# The arguments are decided based on sensible defaults as well as the current values of the 'arg_*' variables.
function setBxlCmdArgs {
    g_bxlCmdArgs=(
        # some defaults
        /sandboxKind:none
        /remoteTelemetry+
        /enableIncrementalFrontEnd-
        /useHardLinks-
        # some environment variables
        "/p:BUILDXL_BIN=${arg_BuildXLBin}"
        "/p:DOTNET_EXE=$(which dotnet)"
        "/p:MONO_EXE=$(which mono)"
        # user-specified config files
        "/cacheConfigFilePath:$arg_CacheConfigFile"
        "/c:$arg_MainConfig"
        # all other user-specified args
        "${arg_Positional[@]}"
    )
}

# Greps system log for messages from BuildXLSandbox kext.
#
# Takes one argument, which is the interval passed to 'log show --last'.
#
# Prints outs: (1) total number of messages, (2) number of "send retries", and (3) number of errors.
# If errors are found, they are printed out to stdout.
#
# Returns 1 if either no log messages are found or non-zero errors are found, and 0 otherwise.
function checkKextLog {
    local logInterval="$1"

    print_info "Checking system log for kext error messages"

    local kextLogFile="kext-log.txt"
    log show --last $logInterval --predicate 'message contains "buildxl"' > $kextLogFile

    local numKextLogLines=$(wc -l $kextLogFile | awk '{print $1}')
    local numKextErrors=$(grep "ERROR" $kextLogFile | wc -l | awk '{print $1}')

    print_info "System log stats :: total messages: $numKextLogLines | num errors: $numKextErrors"

    local status=0

    if [[ $numKextErrors != 0 ]]; then
        print_error "Kext error messages found:"
        grep "ERROR" $kextLogFile
        status=1
    fi

    rm $kextLogFile
    return $status
}

# Runs a BuildXL build.
#
# Performs a number of checks prior to running BuildXL (like checking that the required options have been specified,
# that the BuildXL deployment folder contains the necessary files, etc.).
#
# Optionally symlinks Sdk.Transformers module from the BuildXL deployment folder into the
# specified target folder.
#
# When all checks pass, it runs BuildXL and returns the same exit code returned by BuildXL.
function build {
    print_info "Checking BuildXL bin folder"
    if [[ -z "$arg_BuildXLBin" ]]; then
        print_info "using the location of this script as BUILDXL_BIN: ${MY_DIR}"
        arg_BuildXLBin="${MY_DIR}"
    fi

    if [[ ! -d "$arg_BuildXLBin" ]]; then
        print_error "'BUILDXL_BIN' must point to a folder; '$arg_BuildXLBin' is not a folder"
        return $ERROR_BUILDXL_BIN_NOT_FOLDER
    fi

    if [[ -z $arg_CacheConfigFile ]]; then
        arg_CacheConfigFile="$arg_BuildXLBin/$DEFAULT_CACHE_CONFIG_FILE_NAME"
    fi

    if [[ ! -f "$arg_CacheConfigFile" ]]; then
        print_error "Cache config file not found: '$arg_CacheConfigFile'"
        return $ERROR_BUILDXL_FILE_NOT_PRESENT
    fi

    local bxlFilesToCheck="bxl bxl.runtimeconfig.json bxl.deps.json"
    for f in $bxlFilesToCheck; do
        if [[ ! -f $arg_BuildXLBin/$f ]]; then
            print_error "Expected to find file '$f' in '$arg_BuildXLBin' but that file is not present"
            return $ERROR_BUILDXL_FILE_NOT_PRESENT
        fi
    done

    # convert arg_BuildXLBin to absolute path before creating symlinks
    arg_BuildXLBin=$(cd "$arg_BuildXLBin" && pwd)
    print_info "BUILDXL_BIN set to $arg_BuildXLBin"

    # load kext if arg_loadKext is not empty
    if [[ -n "$arg_loadKext" ]]; then
        readonly kextPath="$arg_BuildXLBin/native/MacOS/BuildXLSandbox.kext"
        if [[ ! -d "$kextPath" ]]; then
            print_error "Kext folder not found at '$kextPath'"
            exit 1
        fi
        print_info "Loading kext from: '$kextPath'"
        sudo "${MY_DIR}/sandbox-load.sh" --deploy-dir /tmp "$kextPath"
        if [[ "$?" != 0 ]]; then
            print_error "Could not load BuildXLSandbox kernel extension"
            exit 1
        fi
    fi

    # Create symlinks for Sdk.Transformers dirs
    if [[ -n "$arg_SymlinkSdksInto" ]]; then
        for bxlSdkDir in "$arg_BuildXLBin/Sdk/Sdk.Transformers"; do
            local mySdkDir="$arg_SymlinkSdksInto/$(basename $bxlSdkDir)"
            # delete symlink if already exists
            if [[ -L $mySdkDir ]]; then
                rm -rf $mySdkDir
            fi

            # create symlink if nothing exists with the same name
            if [[ ! -e $mySdkDir ]]; then
                print_info "Symlinking sdk folder from BuildXL deployment: $mySdkDir -> $bxlSdkDir"
                ln -s "$bxlSdkDir" "$mySdkDir"
            else
                print_error "File/folder '$mySdkDir' already exists.  Please remove this folder since this script needs to symlink a built-in SDK to that location."
                return $ERROR_CANNOT_SYMLINK_SDK
            fi
        done
    fi

    if [[ -z "$arg_MainConfig" ]]; then
        if [[ -z $arg_loadKext ]]; then
            print_warning "Switch --config not specified --> no BuildXL build to run"
        fi
        return 0
    fi

    setBxlCmdArgs

    local bxlExe="$arg_BuildXLBin/bxl"
    chmod u=rx "$bxlExe"

    print_info "${tputBold}Running bxl:${tputReset} '$bxlExe' ${g_bxlCmdArgs[@]}"

    "$bxlExe" "${g_bxlCmdArgs[@]}"
    local bxlExitCode=$?

    if [[ $bxlExitCode == 0 ]]; then
        echo "${tputBold}${tputGreen}BuildXL Succeeded${tputReset}"
    else
        echo "${tputBold}${tputRed}BuildXL Failed${tputReset}"
    fi

    local kextLogCheckResult=0
    if [[ ! -z $arg_checkKextLogInterval ]]; then
        checkKextLog $arg_checkKextLogInterval
        kextLogCheckResult=$?
    fi

    if [[ $kextLogCheckResult != 0 ]]; then
        print_error "Kext errors found"
        return 1
    fi

    return $bxlExitCode
}

function printHelp {
    groff -man -Tascii "${BASH_SOURCE[0]}.1"
}

function parseArgs {
    arg_Positional=()
    while [[ $# -gt 0 ]]; do
        cmd="$1"
        case $cmd in
            --help|-h)
                printHelp
                exit 0
                ;;
            --buildxl-bin)
                arg_BuildXLBin="$2"
                shift
                shift
                ;;
            --config)
                arg_MainConfig="$2"
                shift
                shift
                ;;
            --symlink-sdks-into)
                arg_SymlinkSdksInto="$2"
                shift
                shift
                ;;
            --check-kext-log)
                arg_checkKextLogInterval="$2"
                shift
                shift
                ;;
            --cache-config-file)
                arg_CacheConfigFile="$2"
                shift
                shift
                ;;
            --load-kext)
                arg_loadKext="1"
                shift
                ;;
            --no-load-kext)
                arg_loadKext=""
                shift
                ;;
            *)
                arg_Positional+=("$1")
                shift
                ;;
        esac
    done
}

parseArgs "$@"
build
