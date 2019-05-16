#!/bin/bash

readonly MYDIR=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)
readonly buildxlDir=$(cd $MYDIR/../.. && pwd)
readonly sandboxSrcDir="${buildxlDir}/Public/Src/Sandbox/MacOs"

readonly nugetExe=${buildxlDir}/Shared/Tools/NuGet.exe
readonly nugetTemplateDir=$MYDIR/runtime.osx-x64.BuildXL.template
readonly nugetFeed="https://cloudbuild.pkgs.visualstudio.com/_packaging/BuildXL.Selfhost/nuget/v3/index.json"

if [[ ! -d $nugetTemplateDir ]]; then
    echo "[ERROR] Expected to find Nuget template folder at '$nugetTemplateDir' but no such directory exists"
    exit 1
fi

readonly version=$1

if [[ -z $version ]]; then
    echo "[ERROR] Must supply version"
    exit 1
fi

readonly PKG_BASE_NAME=runtime.osx-x64.BuildXL

readonly KEXT_NAME=BuildXLSandbox.kext
readonly COREDUMPTESTER_NAME=CoreDumpTester
readonly ARIA_DYLIB_NAME=libBuildXLAria.dylib
readonly INTEROP_DYLIB_NAME=libBuildXLInterop.dylib
readonly MONITOR_NAME=SandboxMonitor

function updateFileAtLine() {
    local _srcFile=$1
    local _lineNumber=$2
    local _srcPattern=$3
    local _replacementStr=$4

    if [[ ! -f $_srcFile ]]; then
        echo "[ERROR] File '$_srcFile' not found"
        exit 1
    fi

    if [[ -z $lineToReplace ]]; then
        echo "[ERROR] no line matching key '$_lineKey' found in file '$_srcFile'"
        exit 1
    fi

    local sedCmd=$(echo "sed -i.bak '${_lineNumber}s!${_srcPattern}!${_replacementStr}!g' $_srcFile")
    echo $sedCmd | /bin/bash
    if [[ $? != 0 ]]; then
        echo "[ERROR] sed command failed: $sedCmd"
        exit 1
    fi

    echo "Updated ${_srcFile}:${_lineNumber}"
    echo "  $(sed $_lineNumber'q;d' ${_srcFile}.bak)"
    echo "to"
    echo "  $(sed $_lineNumber'q;d' $_srcFile)"

    rm "${_srcFile}.bak"
}

function updateSingleLineInFile() {
    local _srcFile=$1
    local _lineKey=$2
    local _srcPattern=$3
    local _replacementStr=$4

    if [[ ! -f $_srcFile ]]; then
        echo "[ERROR] File '$_srcFile' not found"
        exit 1
    fi

    local lineToReplace=$(grep -n "$_lineKey" $_srcFile | head -n1 | cut -d: -f1)
    updateFileAtLine "$_srcFile" $lineToReplace "$_srcPattern" "$_replacementStr"
}

function updateKextVersionSourceFile() {
    local _newVersion=$1

    local _srcFile=${sandboxSrcDir}/Sandbox/Src/Resources/Info.plist
    if [[ ! -f $_srcFile ]]; then
        echo "[ERROR] File '$_srcFile' not found"
        exit 1
    fi

    local key="<key>CFBundleVersion</key>"
    local matchLineNumber=$(grep -n "$key" $_srcFile | cut -d: -f1)
    if [[ -z $matchLineNumber ]]; then
        echo "[ERROR] no version key ('$key') found in file '$_srcFile'"
        exit 1
    fi

    local lineToReplace=$matchLineNumber
    ((lineToReplace++))

    updateFileAtLine "$_srcFile" $lineToReplace "<string>.*</string>" "<string>$_newVersion</string>"
}

function updateBuildXLConfigDscFile() {
    local _newVersion=$1

    updateSingleLineInFile          \
        ${buildxlDir}/config.microsoftInternal.dsc    \
        "runtime.osx-x64.BuildXL"   \
        'version: ".*"'             \
        'version: "'$_newVersion'"'
}

function updateRequiredKextVersion() {
    local _newVersion=$1

    updateSingleLineInFile                                                   \
        ${buildxlDir}/Public/Src/Engine/Processes/SandboxedKextConnection.cs \
        "public const string RequiredKextVersionNumber = "                   \
        ' = ".*"'                                                            \
        ' = "'$_newVersion'"'
}

function buildBuildXLBinaries() {
    local _buildScript=${buildxlDir}/bxl.sh

    if [[ ! -f $_buildScript ]]; then
        echo "[ERROR] Build script not found: $_buildScript"
        exit 1
    fi

    echo "Building macOS binaries for private runtime package"

    kext="/f:output='*/osx-x64/native/macOS/*'or"
    libs="output='*/osx-x64/libBuildXL*'or"
    monitor="output='*/osx-x64/SandboxMonitor'or"
    tester="output='*/osx-x64/TestProj/tests/shared_bin/TestProcess/MacOs/CoreDumpTester'"

    local args=(
        --internal
        $kext$libs$monitor$tester
        /q:DebugDotNetCoreMac
        /q:ReleaseDotNetCoreMac
        /sandboxKind:macOsKext
        /scrub
    )

    $_buildScript "${args[@]}"

    if [[ $? != 0 ]]; then
        echo "[ERROR] macOS binaries for private runtime package [$_configuration]"
        exit 1
    fi
}

function prepareNugetDestinationFolder() {
    local _destDir=$1

    if [[ -z $_destDir ]]; then
        echo "[ERROR] must specify Nuget destination dir"
        exit 1
    fi

    if [[ -d $_destDir ]]; then
        echo "scrubbing destination: $_destDir"
        rm -rf $_destDir
    fi

    if [[ ! -d $nugetTemplateDir ]]; then
        echo "[ERROR] Nuget template dir not found: '$nugetTemplateDir'"
        exit 1
    fi

    echo "copy $nugetTemplateDir --> $_destDir"
    rsync -r --exclude '.gitignore' $nugetTemplateDir/ $_destDir
}

function copyBinariesForConfiguration() {
    local _ariaDylibFile="$1"
    local _interopDylibFile="$2"
    local _coredumptesterExe="$3"
    local _monitorExe="$4"
    local _kextDir="$5"
    local _destDir="$6"

    if [[ ! -f $_ariaDylibFile ]]; then
        echo "[ERROR] Dylib not found: $_ariaDylibFile"
        exit 1
    fi

    if [[ ! -f $_interopDylibFile ]]; then
        echo "[ERROR] Dylib not found: $_interopDylibFile"
        exit 1
    fi

    if [[ ! -f $_coredumptesterExe ]]; then
        echo "[ERROR] core dump tester not found: $_coredumptesterExe"
        exit 1
    fi

    if [[ ! -f $_monitorExe ]]; then
        echo "[ERROR] SandboxMonitor executable not found: $_monitorExe"
        exit 1
    fi

    if [[ ! -d $_kextDir ]]; then
        echo "[ERROR] kext folder not found: $_kextDir"
        exit 1
    fi

    if [[ ! -d $_destDir ]]; then
        echo "dest dir does not exist: '$_destDir'"
        read -p "Create dir (y/n)?" choice
        case "$choice" in
            y|Y ) mkdir -p $_destDir ;;
            n|N ) exit 1            ;;
              * ) exit 1            ;;
        esac
    fi

    echo "  - scrubbing $_destDir"
    rm -rf "$_destDir"/*

    echo "  - deploying aria lib $_ariaDylibFile --> $_destDir"
    cp "$_ariaDylibFile" "$_destDir"/

    echo "  - deploying interop lib $_interopDylibFile --> $_destDir"
    cp "$_interopDylibFile" "$_destDir"/

    echo "  - deploying core dump tester exe $_coredumptesterExe --> $_destDir"
    cp "$_coredumptesterExe" "$_destDir"/

    echo "  - deploying monitor exe $_monitorExe --> $_destDir"
    cp "$_monitorExe" "$_destDir"/

    echo "  - deploying kext dir $_kextDir --> $_destDir"
    cp -r "$_kextDir" "$_destDir"/

    if [[ -d ${_kextDir}.dSYM ]]; then
        echo "  - deploying dSYM files ${_kextDir}.dSYM --> $_destDir"
        cp -r ${_kextDir}.dSYM $_destDir/
    fi
}

function prepareKextForSigning() {
    local _kextDir=$1
    local _outZipFile=$2
    local _replacementDir=$3

    if [[ ! -d $_kextDir ]]; then
        echo "[ERROR] Kext dir not found: $_kextDir"
        exit 1
    fi

    if [[ -z $_outZipFile ]]; then
        echo "[ERROR] Must specify output zip file"
        exit 1
    fi

    echo "Creating a zip file '$_outZipFile' from '$_kextDir' for extension signing"
    rm -f $_outZipFile

    pushd $_kextDir > /dev/null
    cd ..
    zip -r $_outZipFile $(basename $_kextDir)
    popd > /dev/null

    cat <<EOM

---------- only required if releasing signed + notarized binaries ----------

 Upload

    $_outZipFile

for signing and when done, run the 'notarize.sh' script, then replace

    $_replacementDir

with the signed, notarized and stapled KEXT!

----------------------------------------------------------------------------
>>> press enter when done ...
EOM
    read
}

function updateNuspecVersion() {
    local _nuspecFile=$1
    local _version=$2

    if [[ ! -f $_nuspecFile ]]; then
        echo "[ERROR] Nuspec file not found: $_nuspecFile"
        exit 1
    fi

    if [[ -z $_version ]]; then
        echo "[ERROR] Must specify version"
        exit 1
    fi

    echo "Updating version in '$_nuspecFile' to $_version"
    sed -i.bak 's!<version>.*</version>!<version>'$_version'</version>!g' $_nuspecFile
    rm $_nuspecFile.bak
    echo "New version $(grep '<version>' $_nuspecFile)"
}

function createNupkg() {
    local _nugetDir=$1
    local _outNupkgFile=$2

    if [[ ! -d $_nugetDir ]]; then
        echo "[ERROR] Nuget dir not found: $_nugetDir"
        exit 1
    fi

    if [[ -z $_outNupkgFile ]]; then
        echo "[ERROR] Must provide output nupkg file"
        exit 1
    fi

    rm -f $_outNupkgFile

    echo "Creating nupkg..."
    pushd $_nugetDir > /dev/null

    if [[ -n $(which mono) && -f "$nugetExe" ]]; then
        mono "${nugetExe}" pack -OutputDirectory ../
    else
        echo "Couldn't create Nuget package, aborting!"
        exit 1
    fi

    popd > /dev/null
}

readonly nugetDestDir=$(pwd)/${PKG_BASE_NAME}.${version}
readonly nuspecFile=$nugetDestDir/$PKG_BASE_NAME.nuspec
readonly nupkgFileName=${PKG_BASE_NAME}.${version}.nupkg

echo "# ====================================="
echo "# Nuget Destination:"
echo "#   - $nugetDestDir"
echo "#"
echo "# Nuget Version"
echo "#   - $version"
echo "# ====================================="
echo

prepareNugetDestinationFolder $nugetDestDir

# Must update the version in Info.plist before building
# (because that should be the version of the published kext)
#
# Must NOT update the version in config.dsc before building
# (because the build would try to download that package that hasn't been published yet).
updateKextVersionSourceFile $version
updateRequiredKextVersion $version
buildBuildXLBinaries
updateBuildXLConfigDscFile $version

for conf in debug release
do
    declare destDir=$(find $nugetDestDir -iname $conf)
    if [[ -z $destDir ]]; then
        echo "[ERROR] Folder '$conf' not found inside nuget destination dir: '$nugetDestDir'; this folder should have been coppied from the nuget template dir '$nugetTemplateDir'"
        exit 1
    fi

    # The CoreDumpTester is there until we have all tests running through BuildXL itself and can later be deleted
    copyBinariesForConfiguration                                                                                   \
        "$buildxlDir/Out/Bin/$conf/osx-x64/$ARIA_DYLIB_NAME"                                                       \
        "$buildxlDir/Out/Bin/$conf/osx-x64/$INTEROP_DYLIB_NAME"                                                    \
        "$buildxlDir/Out/Bin/tests/$conf/osx-x64/TestProj/tests/shared_bin/TestProcess/MacOs/$COREDUMPTESTER_NAME" \
        "$buildxlDir/Out/Bin/$conf/osx-x64/$MONITOR_NAME"                                                          \
        "$buildxlDir/Out/Bin/$conf/osx-x64/native/macOS/$KEXT_NAME"                                                \
        $destDir

    if [[ $conf == "release" ]]; then
        prepareKextForSigning                                           \
            "$buildxlDir/Out/Bin/$conf/osx-x64/native/macOS/$KEXT_NAME" \
            $(pwd)/BuildXLSandbox-Release-$(date +%Y-%m-%d).zip         \
            "$destDir/$KEXT_NAME"
    fi
done

updateNuspecVersion $nuspecFile $version

readonly nupkgFile=$(pwd)/$nupkgFileName
createNupkg $nugetDestDir $nupkgFile

echo "Removing dest dir $nugetDestDir"
rm -rf $nugetDestDir

if [[ -n $(which mono) && -f "$nugetExe" ]]; then
    echo "Publishing ${nupkgFile} to ${nugetFeed}"
    mono "${nugetExe}" push "${nupkgFile}" -Source "${nugetFeed}" -ApiKey "AzureDevOps"
else
    echo " !!! Must publish $nupkgFile manually to feed: '${nugetFeed}'"
fi
