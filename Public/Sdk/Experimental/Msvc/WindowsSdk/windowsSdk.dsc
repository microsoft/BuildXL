// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";

export declare const qualifier: { platform: "x86" | "x64"};

// CODESYNC: This version should be updated together with the version number of the Windows SDK nuget packages in config.dsc
// NOTE: this version is not strictly the same as the version number for the package. The first three numbers should be the same,
// but the final number maybe different.
const version = "10.0.22621.0";

const isWin = Context.getCurrentHost().os === "win";
const sdkHeaders = importFrom("Microsoft.Windows.SDK.cpp").Contents.all;
const sdkLibsX86 = importFrom("Microsoft.Windows.SDK.CPP.x86").Contents.all;
const sdkLibsX64 = importFrom("Microsoft.Windows.SDK.CPP.x64").Contents.all;

namespace UM {
    @@public
    export const include: StaticDirectory = Transformer.reSealPartialDirectory(sdkHeaders, r`c/Include/${version}/um`, "win");

    @@public
    export const lib: StaticDirectory = Transformer.reSealPartialDirectory(getArchitectureSpecificLibraries(), r`c/um/${qualifier.platform}`, "win");

    @@public
    export const standardLibs: File[] = [
        ...addIfLazy(isWin, () => [
            lib.getFile(r`advapi32.lib`),
            lib.getFile(r`kernel32.lib`),
            lib.getFile(r`ntdll.lib`),
            lib.getFile(r`pathcch.lib`),
            lib.getFile(r`uuid.lib`),
            lib.getFile(r`ktmw32.lib`)
        ])
    ];
}

namespace Shared {
    @@public
    export const include: StaticDirectory = Transformer.reSealPartialDirectory(sdkHeaders, r`c/Include/${version}/shared`, "win");
}

namespace Ucrt {
    @@public
    export const include: StaticDirectory = Transformer.reSealPartialDirectory(sdkHeaders, r`c/Include/${version}/ucrt`, "win");

    @@public
    export const lib: StaticDirectory = Transformer.reSealPartialDirectory(getArchitectureSpecificLibraries(), r`c/ucrt/${qualifier.platform}`, "win");
}

function getArchitectureSpecificLibraries() : StaticDirectory {
    if (!isWin) {
        return undefined;
    }

    switch (qualifier.platform) {
        case "x86":
            return sdkLibsX86;
        case "x64":
            return sdkLibsX64;
        default:
            Contract.fail(`Unknown platform for Windows SDK ${qualifier.platform}`);
    }
}