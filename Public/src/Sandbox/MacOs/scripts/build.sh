#!/bin/bash

# compute DOMINO_HOME assuming the folder of this script to be $DOMINO_HOME/MacOs/scripts
readonly SCRIPTS_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)
readonly MACOS_DIR=$(dirname $SCRIPTS_DIR)
readonly INTEROP_DYLIB_SRC_NAME=libBuildXLInterop.dylib
readonly INTEROP_DYLIB_DEST_NAME=libBuildXLInteropMacOS.dylib

declare arg_conf="Debug"
declare arg_loadKext=""
declare arg_clean=""
declare arg_schemes=()

while [[ $# -gt 0 ]]; do
    case "$1" in
	[rR]elease)
	    arg_conf=Release
	    shift
	    ;;
	[dD]ebug)
	    arg_conf=Debug
	    shift
	    ;;
	--load-kext)
	    arg_loadKext="true"
	    shift
	    ;;
    --scheme)
        arg_schemes+=("$2")
        shift
        shift
        ;;
	--clean)
	    arg_clean="1"
	    shift
	    ;;
	*)
	    shift
	    ;;
    esac
done

readonly outDir="$MACOS_DIR/Out"

# force clean build
if [[ -n $arg_clean ]]; then
    find "$outDir" -type d -name $arg_conf | xargs rm -rf
fi

# if no scheme is specified by the user, add all the schemes
if [[ -z "$arg_schemes" ]]; then
    arg_schemes=(Interop BuildXLSandbox SandboxMonitor)
fi

readonly xcodebuildLog=xcodebuild-out.txt

# build all .xcodeproj
for scheme in "${arg_schemes[@]}"; do
    echo "Building Scheme: $scheme"
    xcodebuild -workspace $MACOS_DIR/BuildXL.xcworkspace -scheme $scheme -configuration $arg_conf -derivedDataPath "$outDir" 2>&1 1>$xcodebuildLog
    if [[ $? != 0 ]]; then
        echo "ERROR: build failed":
        cat xcodebuild-out.txt
        exit 1
    fi
done

rm -f $xcodebuildLog

readonly productsDir=$outDir/Build/Products
readonly dylibFile=$productsDir/$arg_conf/$INTEROP_DYLIB_SRC_NAME
readonly dylibRenamedFile=$productsDir/$arg_conf/$INTEROP_DYLIB_DEST_NAME

if [[ ! -f $dylibFile ]]; then
    echo "ERROR: Dylib file $dylibFile not found"
    exit 2
fi

echo "Copying: $(cp -v $dylibFile $dylibRenamedFile)"

# deploy built dylib into $DOMINO_BIN (if $DOMINO_BIN is defined)
if [[ -d $DOMINO_BIN ]]; then
    readonly dylibDest=$DOMINO_BIN/$INTEROP_DYLIB_DEST_NAME
    if [[ -f $dylibDest ]]; then rm -f $dylibDest; fi
    echo "Copying: $(cp -v $dylibRenamedFile $dylibDest)"
fi

if [[ ! -z $arg_loadKext ]]; then
    sudo bash <<EOF
    $MACOS_DIR/scripts/sandbox-load.sh --kext $productsDir/$arg_conf/BuildXLSandbox.kext --deploy-dir /tmp
EOF
fi