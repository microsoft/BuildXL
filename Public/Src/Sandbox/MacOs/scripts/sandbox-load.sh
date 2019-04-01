#!/bin/bash

MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)

source "$MY_DIR/env.sh"

declare arg_kextSourceDir=""
declare arg_kextDeployDir=""
declare arg_noReload=""
declare arg_enableCounters=""
declare arg_verboseLogging=""
declare arg_buildXLBundleId="com.microsoft.buildxl.sandbox"

# Prints out BuildXLSandbox bundle id if it is currently loaded, or empty string otherwise.
function getRunningBuildXLSandboxBundleId {
    echo $(kextstat | grep -o ${arg_buildXLBundleId})
}

# Unloads BuildXLSandbox extension if already running
function unloadBuildXLSandbox {
    local bundleId=$(getRunningBuildXLSandboxBundleId)
    if [[ ! -z $bundleId ]]; then
        print_info " [Unloading] $bundleId"
        kextunload -bundle-id $bundleId
        if [[ $? != 0 ]]; then
            print_error " Failed to unload existing BuildXLSandbox extension; extension id: $bundleId"
            return 1
        fi
    fi
}

# Copies 'fromDir' to 'toDir', first deleting 'toDir' if it exists.
# WARNING: if 'toDir' exists, it is first deleted.
function redeployKext { # (fromDir, toDir)
    local fromDir="$1"
    local toDir="$2"

    if [[ ! -d $fromDir ]]; then
        print_error "'${fromDir}' is not a folder"
        return 1
    fi

    # delete destination dir if exists
    if [[ -d $toDir ]]; then
        print_info " [Deleting] $toDir"
        rm -rf $toDir
    fi

    # deploy extension and setting permissions
    print_info " [Deploying] $fromDir --> $toDir"
    cp -r "$fromDir" "$toDir"
}

# Loads BuildXLSandbox extension from 'kextDir' directory by calling 'kextload'.
function loadBuildXLSandbox { # (kextDir)
    local kextDir="$1"

    if [[ -z $kextDir ]]; then
        print_error "No kext folder specified"
        return 1
    fi

    if [[ ! -d "$kextDir" ]]; then
        print_error "'${kextDir}' is not a directory"
        return 1
    fi

    # set permissions
    chown -R root:wheel "$kextDir"
    chmod -R 555 "$kextDir"

    # load extension
    print_info " [Loading] $kextDir"
    kextload "$kextDir"

    # verify extension loaded
    local runningExt=$(getRunningBuildXLSandboxBundleId)
    if [[ -z $runningExt ]]; then
        print_error " Failed to load BuildXLSandbox from $kextDir; see more details below"
        kextutil "$kextDir"
        return 1
    else
        print_info " [Loaded] $runningExt"
    fi
}

function printHelp {
    man "${BASH_SOURCE[0]}.1" | cat
}

# Parses the command line arguments into the arg_* variable defined at the top of this script
function parseArgs {
    while [[ $# -gt 0 ]]; do
        local cmd="$1"
        case $cmd in
            --help|-h)
                printHelp
                exit 0
                ;;
            --kext)
                arg_kextSourceDir="$2"
                shift
                shift
                ;;
            --deploy-dir)
                arg_kextDeployDir="$2"
                shift
                shift
                ;;
            --no-reload)
                arg_noReload=1
                shift
                ;;
            --kext-bundle-id)
                arg_buildXLBundleId="$2"
                shift
                shift
                ;;
            --enable-counters)
                arg_enableCounters="1"
                shift
                ;;
            --verbose-logging)
                arg_verboseLogging="1"
                shift
                ;;
            *)
                if [[ $# == 1 && -z $arg_kextSourceDir ]]; then
                    arg_kextSourceDir="$1"
                    shift
                else
                    print_error "Using a positional argument ('$1') can only be done at the end of the command line when no --kext switch was used before; execute '$0 --help' for usage."
                    exit 1
                fi
                ;;
        esac
    done
}

function validateArgs {
    if [[ -z $arg_kextSourceDir ]]; then
        print_error "Kext folder must be specified; run '$0 --help' for usage."
        return 1
    fi
}

# ======== entry point =======

parseArgs "$@" && validateArgs
if [[ $? != 0 ]]; then
    exit 1
fi

if [[ $EUID -ne 0 ]]; then
   print_error "This script must be run as root"
   exit -1
fi

# just exit if already loaded and --no-reload
readonly runningExtBundleId=$(getRunningBuildXLSandboxBundleId)
if [[ -n $runningExtBundleId && -n $arg_noReload ]]; then
    print_info "BuildXLSandbox is alredy running: $runningExtBundleId"
    exit 0
fi

# unload currently loaded extension (if any)
unloadBuildXLSandbox || exit 1

# optionally redeploy kext to a new folder
declare finalKextFolder=""
if [[ -z "$arg_kextDeployDir" ]]; then
    finalKextFolder="${arg_kextSourceDir}"
else
    if [[ ! -d $arg_kextDeployDir ]]; then
        print_error "Provided destination location is not a directory: '${arg_kextDeployDir}'"
        exit 1
    fi
    finalKextFolder="${arg_kextDeployDir}/BuildXLSandbox.kext"
    redeployKext "$arg_kextSourceDir" "$finalKextFolder" || exit 1
fi

# load kext
loadBuildXLSandbox "$finalKextFolder"

# enable counters if requested
if [[ -n $arg_enableCounters ]]; then
    sysctl kern.bxl_enable_counters=1
fi

# enable verbose logging if requested
if [[ -n $arg_verboseLogging ]]; then
    sysctl kern.bxl_verbose_logging=1
fi