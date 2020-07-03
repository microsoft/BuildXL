#!/bin/bash

readonly __dir=$(cd `dirname ${BASH_SOURCE[0]}` && pwd)

set -e
set -u

function onExit {
    popd > /dev/null 2>&1
}

readonly version="$1"
readonly pkgName="runtime.linux-x64.BuildXL.${version}"

pushd "${__dir}" > /dev/null 2>&1
trap onExit EXIT

cd "${__dir}"
make debug release

cd bin

rm -rf "${pkgName}"
mkdir -p "${pkgName}/ref/netstandard"
touch "${pkgName}/ref/netstandard/_._"

mkdir -p "${pkgName}/runtimes/linux-x64/native"
cp -r debug release "${pkgName}/runtimes/linux-x64/native"

cat > "${pkgName}/runtime.linux-x64.buildxl.nuspec" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd">
  <metadata minClientVersion="2.12">
    <id>runtime.linux-x64.BuildXL</id>
    <version>${version}</version>
    <title>runtime.linux-x64.BuildXL</title>
    <authors>Microsoft</authors>
    <owners>microsoft,buildxl,bxl</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <description>The BuildXL runtime libraries for Linux.</description>
    <copyright>© Microsoft Corporation. All rights reserved.</copyright>
    <serviceable>true</serviceable>
  </metadata>
</package>
EOF

cd "${pkgName}"
zip -v ../${pkgName}.nupkg *