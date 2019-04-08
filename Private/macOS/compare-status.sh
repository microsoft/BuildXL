#!/bin/bash

MY_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)

set -e

declare arg_StatusFiles=()
declare arg_Fields=()
declare arg_Term="svg"

function error {
    echo "[ERROR]: $@"
}

function warning {
    echo "[WARNING]: $@"
}

function parseArgs {
    while [[ $# -gt 0 ]]; do
        cmd="$1"
        case $cmd in
            --term)
                shift
                arg_Term="$1"
                shift
                ;;
            --files)
                shift
                while [[ $# -gt 0 && $1 != --* ]]; do
                    arg_StatusFiles+=("$1")
                    shift
                done
                ;;
            --fields)
                shift
                while [[ $# -gt 0 && $1 != --* ]]; do
                    arg_Fields+=("$1")
                    shift
                done
                ;;
            *)
                error "Unknown argument: $1"
                return 1
                ;;
        esac
    done
}

declare final_StatusFiles=()

function validateArgs {
    [[ ${#arg_StatusFiles[@]} -gt 0 ]] || (error "No status files specified" && return 1)
    [[ ${#arg_Fields[@]} -gt 0 ]] || (error "No fields specified" && return 1)
    
    for st in "${arg_StatusFiles[@]}"; do
        [[ -d "$st" ]] && st="$st/BuildXL.status.csv"
        [[ -f "$st" ]] || (error "Not a BuildXL status file: $st" && return 1)
        final_StatusFiles+=("$st")
    done
}

parseArgs "$@"
validateArgs

for field in "${arg_Fields[@]}"; do
    _files="${final_StatusFiles[@]}"
    gnuplot -e "_files='${_files}'; _col='${field}'; _term='${arg_Term}'" "$MY_DIR/plot.gp"
done