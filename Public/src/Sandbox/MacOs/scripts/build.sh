#!/bin/bash

readonly SCRIPTS_DIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)
readonly MACOS_DIR=$(dirname $SCRIPTS_DIR)
readonly INTEROP_DYLIB_SRC_NAME=libBuildXLInterop.dylib
readonly INTEROP_DYLIB_DEST_NAME=libBuildXLInteropMacOS.dylib

declare arg_conf="Debug"
declare arg_loadKext=""

while [[ $# -gt 0 ]]; do
    case "$1" in
	[rR]elease)
	    arg_conf=Release
	    shift
	    ;;
	--load-kext)
	    arg_loadKext="true"
	    shift
	    ;;
	*)
	    shift
	    ;;
    esac
done

# force clean build
find $MACOS_DIR/ -type d -name $arg_conf | xargs rm -rf

# build all .xcodeproj
for projectDir in `find $MACOS_DIR/ -type d -name "*.xcodeproj"`; do
    xcodebuild -project $projectDir -configuration $arg_conf
    if [[ $? != 0 ]]; then
        echo "ERROR: Build failed"
        exit 1
    fi
done

readonly dylibFile=$MACOS_DIR/Interop/build/$arg_conf/$INTEROP_DYLIB_SRC_NAME
readonly dylibRenamedFile=$MACOS_DIR/Interop/build/$arg_conf/$INTEROP_DYLIB_DEST_NAME

if [[ ! -f $dylibFile ]]; then
    echo "ERROR: Dylib file $dylibFile not found"
    exit 2
fi

mv -v $dylibFile $dylibRenamedFile

# deploy built dylib into $BUILDXL_BIN (if $BUILDXL_BIN is defined)
if [[ -d $BUILDXL_BIN ]]; then
    readonly dylibDest=$BUILDXL_BIN/$INTEROP_DYLIB_DEST_NAME
    if [[ -f $dylibDest ]]; then rm -f $dylibDest; fi
    cp -v $dylibRenamedFile $dylibDest
fi

if [[ ! -z $arg_loadKext ]]; then
    sudo bash <<EOF
    $MACOS_DIR/scripts/sandbox-load.sh --kext $MACOS_DIR/BuildXLSandbox/build/$arg_conf/BuildXLSandbox.kext --deploy-dir /tmp
EOF
fi