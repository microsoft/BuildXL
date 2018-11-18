#!/bin/bash

if [[ "$TERM" == "xterm-256color" ]]; then
    tputBold=`tput bold`
    tputRed=`tput setaf 1`
    tputGreen=`tput setaf 2`
    tputBlue=`tput setaf 4`
    tputMagenta=`tput setaf 5`
    tputCyan=`tput setaf 6`
    tputClearLine=`tput el`
    tputLineUp=`tput cuu 1`
    tputReset=`tput sgr0`
    tputSaveCursor=`tput sc`
    tputRestoreCursor=`tput rc`
else
    tputBold=""
    tputRed=""
    tputGreen=""
    tputBlue=""
    tputMagenta=""
    tputCyan=""
    tputClearLine=""
    tputLineUp=""
    tputReset=""
    tputSaveCursor=""
    tputRestoreCursor=""
fi

function print_error { # (errorMessage)
    local errorMessage=$@
    echo "${tputRed}${tputBold}[error]${tputReset} ${errorMessage}"
}

function print_warning { # (message)
    local message=$@
    echo "${tputMagenta}${tputBold}[warning]${tputReset} ${message}"
}

function print_info { # (message)
    local message=$@
    echo "${tputBlue}[info]${tputReset} ${message}"
}