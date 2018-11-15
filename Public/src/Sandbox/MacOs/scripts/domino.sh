#!/bin/bash

#!/bin/bash

MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)

source "$MY_DIR/env.sh"

declare ERROR_DOMINO_BIN_NOT_DEFINED=-1
declare ERROR_DOMINO_BIN_NOT_FOLDER=-2
declare ERROR_DOMINO_FILE_NOT_PRESENT=-3
declare ERROR_CANNOT_SYMLINK_SDK=-4
declare ERROR_CONFIG_NOT_SPECIFIED=-5

declare CACHE_CONFIG_FILE_NAME=DominoDefaultCacheConfig.json

declare arg_Positional=()
declare arg_DominoBin=""
declare arg_MainConfig=""
declare arg_SymlinkSdksInto=""
declare arg_checkKextLogInterval=""
declare arg_loadKext=""

# Prints out default Domino invocation command line.
#
# It uses globally declared constants and "arg_*" variables.
function getDominoCmd {
    # NOTES for running Domino:
    #   - must pass /server-
    #   - must use in-memory cache
    #   - /unsafe_DisableDetours is used by default, but can be manually turned off
    #   - ignoring warnings:
    #       - DX0909: experimental options used
    #       - DX2840: failed to read deployment manifest
    #       - DX0900: file access monitoring disabled
    #       - DX0920: detours disabled
    #       - DX0222: file being used as a source file but not under source mount
    #       - DX2825: the execution log might not be usable (cannot find PreviousInputsJournalCheckpoint)
    echo "$arg_DominoBin/bxl"                                      \
        /server- /nowarn:0909,2840,0900,0920,0222,2825                \
        /cacheConfigFilePath:"$arg_DominoBin/$CACHE_CONFIG_FILE_NAME" \
        /unsafe_DisableDetours                                        \
        /remoteTelemetry+                                             \
        /useHardLinks-                                                \
        /enableIncrementalFrontEnd-                                   \
        /p:DOMINO_BIN="$arg_DominoBin" /p:DOTNET_EXE="$(which dotnet)"\
        /c:"$arg_MainConfig"
}

# Greps system log for messages from DominoSandbox kext.
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
    log show --last $logInterval --predicate 'eventMessage contains "DominoSandbox"' | grep "domino_Sandbox" > $kextLogFile

    local numKextLogLines=$(wc -l $kextLogFile | awk '{print $1}')
    local numKextErrors=$(grep "ERROR" $kextLogFile | wc -l | awk '{print $1}')
    local numSendRetries=$(grep "retrying" $kextLogFile | wc -l | awk '{print $1}')

    print_info "System log stats :: total messages: $numKextLogLines | num send retries: $numSendRetries | num errors: $numKextErrors"

    local status=0

    if [[ $numKextErrors != 0 ]]; then
        print_error "Kext error messages found:"
        grep "ERROR" $kextLogFile
        status=1
    fi

    rm $kextLogFile
    return $status
}

# Runs a Domino build.
#
# Performs a number of checks prior to running Domino (like checking that the required options have been specified,
# that the Domino deployment folder contains the necessary files, etc.).
#
# Optionally symlinks Sdk.Prelude and Sdk.Transformers modules from the Domino deployment folder into the
# specified target folder.
#
# When all checks pass, it runs Domino and returns the same exit code returned by Domino.
function build { #(extraDominoArgs)
    local extraDominoArgs=$@

    print_info "Checking Domino bin folder"
    if [[ -z "$arg_DominoBin" ]]; then
        print_info "using the location of this script as DOMINO_BIN: ${MY_DIR}"
        arg_DominoBin="${MY_DIR}"
    fi

    if [[ ! -d "$arg_DominoBin" ]]; then
        print_error "'DOMINO_BIN' must point to a folder; '$arg_DominoBin' is not a folder"
        return $ERROR_DOMINO_BIN_NOT_FOLDER
    fi

    local dominoFilesToCheck="bxl bxl.runtimeconfig.json bxl.deps.json $CACHE_CONFIG_FILE_NAME"
    for f in $dominoFilesToCheck; do
        if [[ ! -f $arg_DominoBin/$f ]]; then
            print_error "Expected to find file '$f' in '$arg_DominoBin' but that file is not present"
            return $ERROR_DOMINO_FILE_NOT_PRESENT
        fi
    done

    # convert arg_DominoBin to absolute path before creating symlinks
    arg_DominoBin=$(cd "$arg_DominoBin" && pwd)
    print_info "DOMINO_BIN set to $arg_DominoBin"

    # load kext if arg_loadKext is not empty
    if [[ ! -z "$arg_loadKext" ]]; then
        readonly kextPath="$arg_DominoBin/native/MacOS/DominoSandbox.kext"
        if [[ ! -d "$kextPath" ]]; then
            print_error "Kext folder not found at '$kextPath'"
            exit 1
        fi
        print_info "Loading kext from: '$kextPath'"
        sudo bash <<EOF
        ${MY_DIR}/sandbox-load.sh --deploy-dir /tmp "$kextPath"
EOF
        if [[ "$?" != 0 ]]; then
            print_error "Could not load DominoSandbox kernel extension"
            exit 1
        fi
    fi

    # Create symlinks for Sdk.Transformers dirs
    if [[ ! -z "$arg_SymlinkSdksInto" ]]; then
        for dominoSdkDir in "$arg_DominoBin/Sdk/Sdk.Transformers"; do
            local mySdkDir="$arg_SymlinkSdksInto/$(basename $dominoSdkDir)"
            # delete symlink if already exists
            if [[ -L $mySdkDir ]]; then
                rm -rf $mySdkDir
            fi

            # create symlink if nothing exists with the same name
            if [[ ! -e $mySdkDir ]]; then
                print_info "Symlinking sdk folder from Domino deployment: $mySdkDir -> $dominoSdkDir"
                ln -s "$dominoSdkDir" "$mySdkDir"
            else
                print_error "File/folder '$mySdkDir' already exists.  Please remove this folder since this script needs to symlink a built-in SDK to that location."
                return $ERROR_CANNOT_SYMLINK_SDK
            fi
        done
    fi

    if [[ -z "$arg_MainConfig" ]]; then
        print_warning "Switch --config not specified --> no Domino build to run"
        return 0
    fi

    local dominoCmd="$(getDominoCmd) $extraDominoArgs"
    print_info "${tputBold}Running bxl:${tputReset} $dominoCmd"
    chmod u=rx "$arg_DominoBin/bxl"
    $dominoCmd
    local dominoExitCode=$?

    if [[ $dominoExitCode == 0 ]]; then
        echo "${tputBold}${tputGreen}Domino Succeeded${tputReset}"
    else
        echo "${tputBold}${tputRed}Domino Failed${tputReset}"
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

    return $dominoExitCode
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
            --domino-bin)
                arg_DominoBin="$2"
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
build ${arg_Positional[@]}
