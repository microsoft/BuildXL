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
declare arg_Runner=()
declare arg_BuildXLBin=""
declare arg_MainConfig=""
declare arg_SymlinkSdksInto=""
declare arg_useAdoBuildRunner=""

declare g_bxlCmdArgs=()
declare g_adoBuildRunnerCmdArgs=()

# Allow for up to 2MB of thread stack size, frontend evaluation stack frames can easily grow beyond the default stack size,
# which is PTHREAD_STACK_MIN for the CLR running on Unix systems
export COMPlus_DefaultStackSize=200000

# Clears and then populates the 'g_bxlArgs' array with arguments to be passed to 'bxl'.
# The arguments are decided based on sensible defaults as well as the current values of the 'arg_*' variables.
function setBxlCmdArgs {
    g_bxlCmdArgs=(
        # some environment variables
        "/p:BUILDXL_BIN=${arg_BuildXLBin}"
        "/p:DOTNET_EXE=$(which dotnet)"
        "/p:MONO_EXE=$(which mono)"
        # user-specified config files
        "/c:$arg_MainConfig"
    )

    if [[ "${OSTYPE}" == "linux-gnu" ]]; then
        g_bxlCmdArgs+=(
            /server-
            /enableEvaluationThrottling
            # setting up core dump creation failed
            /noWarn:460
        )
    fi

    # If we are not using the ado build runner, inject a default cache. Otherwise, we are using
    # the cache config autogen functionality of the runner, so let that kick in
    if [[ -z "$arg_useAdoBuildRunner" ]]; then
        g_bxlCmdArgs+=(
            "/cacheMiss+"
            "/cacheConfigFilePath:$arg_CacheConfigFile"
        )
    else
        g_adoBuildRunnerCmdArgs+=(
            "${arg_Runner[@]}"
        )
    fi

    # all other user-specified args
    g_bxlCmdArgs+=(
        "${arg_Positional[@]}"
    )
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
        return 0
    fi

    setBxlCmdArgs
    local bxlExe="$arg_BuildXLBin/bxl"
    
    # On some usages of this script, execution bits might be
    # missing from the deployment. This is the case, for example, on ADO
    # builds where the engine is deployed by downloading pipeline artifacts.
    # Make sure that the executables that we need in the build are indeed executable.
    chmod u+rx "$bxlExe"
    chmod u+rx "$arg_BuildXLBin/NugetDownloader"
    chmod u+rx "$arg_BuildXLBin/Downloader"
    chmod u+rx "$arg_BuildXLBin/Extractor"

    if [[ -n "$arg_useAdoBuildRunner" ]]; then
        local adoBuildRunnerExe="$arg_BuildXLBin/AdoBuildRunner"
        chmod u=rx "$adoBuildRunnerExe" || true
        print_info "${tputBold}Running AdoBuildRunner:${tputReset} '$adoBuildRunnerExe' ${g_adoBuildRunnerCmdArgs[@]} -- ${g_bxlCmdArgs[@]}"
        "$adoBuildRunnerExe" "${g_adoBuildRunnerCmdArgs[@]}" "--" "${g_bxlCmdArgs[@]}"
    else
        print_info "${tputBold}Running bxl:${tputReset} '$bxlExe' ${g_bxlCmdArgs[@]}"

        "$bxlExe" "${g_bxlCmdArgs[@]}"
    fi
    local bxlExitCode=$?

    if [[ $bxlExitCode == 0 ]]; then
        echo "${tputBold}${tputGreen}BuildXL Succeeded${tputReset}"
    else
        echo "${tputBold}${tputRed}BuildXL Failed${tputReset}"
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
            --cache-config-file)
                arg_CacheConfigFile="$2"
                shift
                shift
                ;;
            --use-adobuildrunner)
                arg_useAdoBuildRunner="1"
                shift
                ;;
            --runner-arg)
                arg_Runner+=("$2")
                shift
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
