#!/bin/bash

declare arg_conf="Debug"

while [[ $# -gt 0 ]]; do
    case "$1" in
	[rR]elease)
	    arg_conf=Release
	    shift
	    ;;
	*)
	    shift
	    ;;
    esac
done

dotnet build BuildXL.MacSandbox.sln --configuration $arg_conf \
    && BUILDXL_BIN=bin/$arg_conf Public/src/Sandbox/MacOs/scripts/build.sh $arg_conf --load-kext