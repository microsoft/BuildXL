#!/bin/bash
# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

### Builds the macOS native library using xcode

arg_projectPath=""
arg_scheme=""
arg_configuration=""
arg_outputDirectory=""
arg_bundlePath=""

parseArgs() {
    while [[ $# -gt 0 ]]; do
        cmd="$1"
        case $cmd in
        --projectPath)
            arg_projectPath="$2"
            shift
            ;;
        --scheme)
            arg_scheme="$2"
            shift
            ;;
        --configuration)
            arg_configuration="$2"
            shift
            ;;
        --outputDirectory)
            arg_outputDirectory="$2"
            shift
            ;;
        --bundlePath)
            arg_bundlePath="$2"
            shift
            ;;
        *)
            shift
            ;;
        esac
    done
}

parseArgs $@

if [[ "$arg_projectPath" == "" || "$arg_scheme"  == "" || "$arg_configuration"  == "" || "$arg_outputDirectory"  == "" || "$arg_bundlePath"  == "" ]]; then
    echo "Usage: $0 --projectPath <path> --scheme <scheme> --configuration <configuration> --outputDirectory <path> --bundlePath <path>"
    exit 1
fi

/usr/bin/xcodebuild build -project $arg_projectPath -scheme $arg_scheme -configuration $arg_configuration -derivedDataPath $arg_outputDirectory -xcconfig $arg_bundlePath -UseModernBuildSystem=YES