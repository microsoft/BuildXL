#!/bin/bash

MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)

set -e

declare arg_MonitorSandbox=""
declare arg_SandboxKinds=()
declare arg_Clean="1"
declare arg_LoadKext="1"
declare arg_PreserveEngineCache=""
declare arg_ApexBuildDir="$MS_BUILD_ROOT"
declare arg_Bxls=()
declare arg_ApexProjects=()
declare arg_BuildXLArgs=()

readonly kSandboxIgnore="MacOsKextIgnoreFileAccesses"
readonly kSandboxKext="MacOsKext"
readonly kSandboxNone="None"

function error {
    echo "[ERROR]: $@"
}

function warning {
    echo "[WARNING]: $@"
}

function parseBoolArg {
    [[ $1 == --no-* ]] && echo "" || echo "1"
}

function parseArgs {
    arg_Positional=()
    while [[ $# -gt 0 ]]; do
        cmd="$1"
        case $cmd in
            --bxls)
                shift
                while [[ $# -gt 0 && $1 != --* ]]; do
                    arg_Bxls+=("$1")
                    shift
                done
                ;;
            --apex-projects)
                shift
                while [[ $# -gt 0 && $1 != --* ]]; do
                    arg_ApexProjects+=("$1")
                    shift
                done
                ;;
            --sandboxKinds)
                shift
                while [[ $# -gt 0 && $1 != --* ]]; do
                    arg_SandboxKinds+=("$1")
                    shift
                done
                ;;
            --buildxlArgs)
                shift
                while [[ $# -gt 0 && $1 != --* ]]; do
                    arg_BuildXLArgs+=("$1")
                    shift
                done
                ;;
            --sandbox)
                arg_SandboxKinds+=($kSandboxKext)
                shift
                ;;
            --sandbox-ignore)
                arg_SandboxKinds+=($kSandboxIgnore)
                shift
                ;;
            --no-sandbox)
                arg_SandboxKinds+=($kSandboxNone)
                shift
                ;;
            --clean|--no-clean)
                arg_Clean=$(parseBoolArg "$cmd")
                shift;
                ;;
            --load-kext|--no-load-kext)
                arg_LoadKext=$(parseBoolArg "$cmd")
                shift
                ;;
            --monitor-sandbox|--no-monitor-sandbox)
                arg_MonitorSandbox=$(parseBoolArg "$cmd")
                shift
                ;;
            --preserve-engine-cache|--no-preserve-engine-cache)
                arg_PreserveEngineCache=$(parseBoolArg "$cmd")
                shift
                ;;
            *)
                error "Unknown argument: $1"
                return 1
                ;;
        esac
    done
}

function checkBxlFolder { # (path)
    local path="$1"
    test -d "$path"                 || (error "Ivalid BXL folder: '$path' is not a folder" && return 1)
    test -f "$path/sandbox-load.sh" || (error "Invalid BXL folder: no 'sandbox-load.sh' found inside '$path'" && return 1)
}

function validateArgs {
    test ${#arg_Bxls[@]} -gt 0         || (error "No BXL folders specified" && return 1)
    test ${#arg_ApexProjects[@]} -gt 0 || (error "No Apex projects specified" && return 1)
    test -d "$arg_ApexBuildDir"        || (error "MS_BUILD_ROOT is not a directory: '$arg_ApexBuildDir'" && return 1)
    for bxl in ${arg_Bxls[@]}; do
        checkBxlFolder "$bxl"
    done
    for sandboxKind in ${arg_SandboxKinds[@]}; do
        [[ $sandboxKind == $kSandboxNone || $sandboxKind == $kSandboxKext || $sandboxKind == $kSandboxIgnore ]] || (error "Unknown sandbox kind: $sandboxKind" && return 1)
    done
}

function clean {
    local devmainDir="$arg_ApexBuildDir/devmain"
    local engineCacheDir="$devmainDir/Domino/Cache.noindex/EngineCache.noindex"
    local bakupEngineCacheDir="$arg_ApexBuildDir/EngineCache.noindex"

    if [[ -n $arg_Clean ]]; then
        if [[ -d "$engineCacheDir" && -n $arg_PreserveEngineCache ]]; then 
            echo "Saving engine cache"
            mv "$engineCacheDir" "$bakupEngineCacheDir"
        fi

        if [[ -d $devmainDir ]]; then
            echo "Deleting $devmainDir"
            rm -rf "$devmainDir"
        fi

        if [[ -d "$bakupEngineCacheDir" ]]; then
            echo "Restoring engine cache"
            mkdir -p $(dirname "${engineCacheDir}")
            mv "$bakupEngineCacheDir" "$engineCacheDir"
        fi
    fi
}

function updateSudoers {
    local sudoersFile="/etc/sudoers.d/buildxl-sandbox-load"
    local tmpSudoersFile="buildxl-sandbox-load"

    rm -f $tmpSudoersFile
    > $tmpSudoersFile
    for bxl in ${arg_Bxls[@]}; do
        #               TAB
        #               vvv
        echo  "$(whoami)	ALL=(root) NOPASSWD:SETENV: $bxl/sandbox-load.sh" >> $tmpSudoersFile
    done

    echo "Need root permissions to overwrite file '$sudoersFile' with the following content:"
    cat $tmpSudoersFile
    chmod 440 $tmpSudoersFile
    sudo cp -f $tmpSudoersFile $sudoersFile
    rm -f $tmpSudoersFile
}

function printConfigurationBanner {
    echo ".------------------------------------------------------------------------------------------------------"
    echo "| Benchmark configuration "
    echo "|   - Sandbox kinds: ${#arg_SandboxKinds[@]}"
    for sandbox in "${arg_SandboxKinds[@]}"; do echo "|      - $sandbox"; done
    echo "|   - APEX projects: ${#arg_ApexProjects[@]}"
    for project in "${arg_ApexProjects[@]}"; do echo "|      - $project"; done
    echo "|   - BXL drops: ${#arg_Bxls[@]}"
    for bxl in "${arg_Bxls[@]}"; do echo "|      - $bxl"; done
    echo "\`------------------------------------------------------------------------------------------------------"
}

function renewKerberosTickets {
    echo "Renewing Kerberos tickets"
    kinit --renew || (error "Couldn't renew Kerberos tickets; run 'kinit --renewable' before running this script" && return 1)
}

function build {
    local bxl="$1"
    local project="$2"
    local sandboxKind="$3"

    local mbuCommand="mbu build --platform mac --domino -c debug -m $project"

    export SDDIFF="diff"
    export DOMINO_BIN_OVERRIDE="$bxl"
    export DOMINO_EXTRA_ARGS="${arg_BuildXLArgs[@]}"

    if [[ $sandboxKind == $kSandboxNone ]]; then
        unset DOMINOCACHE
    else
        export DOMINOCACHE=1
        export DOMINO_SANDBOX_KIND="$sandboxKind"
    fi

    local infoFile="info.txt"
    cat > "$infoFile" <<EOF
========================================================
== Runing a clean MBU build:
==   - project: $project
==   - bxl: $bxl
==   - sandboxing: $sandboxKind
==   - command: $mbuCommand
==   - env{DOMINOCACHE}:         $(env | grep DOMINOCACHE)
==   - env{DOMINO_SANDBOX_KIND}: $(env | grep DOMINO_SANDBOX_KIND)
==   - env{DOMINO_BIN_OVERRIDE}: $(env | grep DOMINO_BIN_OVERRIDE)
==   - env{DOMINO_EXTRA_ARGS}:   $(env | grep DOMINO_EXTRA_ARGS)
========================================================
EOF

    cat "$infoFile"

    # clean outputs
    clean

    # start SandboxMonitor (if requested)
    local monitorPid=0
    local monitorFile="monitor.log"
    if [[ $sandboxKind != $kSandboxNone ]]; then
    if [[ -n $arg_LoadKext ]]; then
            if [[ -d "$bxl/native/MacOS/BuildXLSandbox.kext" ]]; then
        sudo "$bxl/sandbox-load.sh" --kext "$bxl/native/MacOS/BuildXLSandbox.kext" --deploy-dir /tmp
            else
        error "No kext found"
        return 1
            fi
    fi

        if [[ -x "$bxl/SandboxMonitor" && -n $arg_MonitorSandbox ]]; then
            > $monitorFile
            watch -n15 "$bxl/SandboxMonitor --ps-fmt '%cpu,%mem,etime,ucomm' >> $monitorFile" &>/dev/null &
            monitorPid="$!"
            echo "SandboxMonitor($monitorPid) started in background"
        fi
    fi

    # run build
    echo "Running MBU: $mbuCommand"
    $mbuCommand || error "MBU build failed for project '$project', BXL '$(basename $bxl)', and sandbox kind: $sandboxKind"

    # copy logs
    local buildXLLogsDir="$arg_ApexBuildDir/Logs/devmain/Domino"
    local buildXLSaveLogsDir="$arg_ApexBuildDir/Logs/devmain/BuildXL.Bench"

    test -d "$buildXLLogsDir" || (error "BuildXL logs dir not found at '$buildXLLogsDir'" && return 1)
    local latestLogsDir=$(find "$buildXLLogsDir" -type d -depth 1 -exec basename {} \; | sort | tail -n1)
    test -n "$latestLogsDir" || (error "No logs found inside '$buildXLLogsDir'" && return 1)

    mkdir -p $buildXLSaveLogsDir
    local clean=$([[ -n $arg_Clean ]] && echo "clean" || echo "inc")

    local newLogsFolderName=$(echo "$project-$(basename $bxl)-${clean}-${sandboxKind}-$latestLogsDir")
    echo "Saving logs:"
    mv -v "$buildXLLogsDir/$latestLogsDir" "$buildXLSaveLogsDir/$newLogsFolderName"
    mv -v "$infoFile" "$buildXLSaveLogsDir/$newLogsFolderName"
    sd diff > "$buildXLSaveLogsDir/$newLogsFolderName/sd.diff" || (warning "could not save sd diff")

    # shut down SandboxMonitor
    if [[ $monitorPid != 0 && -f "$monitorFile" ]]; then
        echo "Shutting down SandboxMonitor($monitorPid)"
        kill -15 $monitorPid || echo "[WARNING] SandboxMonitor($monitorPid) process not found"
        mv -v $monitorFile "$buildXLSaveLogsDir/$newLogsFolderName"
    fi
}

parseArgs "$@"
validateArgs
printConfigurationBanner
updateSudoers
renewKerberosTickets

for project in "${arg_ApexProjects[@]}"; do
    for sandboxKind in "${arg_SandboxKinds[@]}"; do
        for bxl in "${arg_Bxls[@]}"; do
            build "$bxl" $project $sandboxKind || warning "Build FAILED!"
        done
    done
done