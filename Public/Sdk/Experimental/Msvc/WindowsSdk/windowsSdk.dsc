// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";

export declare const qualifier: { platform: "x86" | "x64"};

const version = "10.0.16299.0";

const isWin = Context.getCurrentHost().os === "win";
const sdk = isWin ? getHeadersAndLibs() : undefined;

namespace UM {
    @@public
    export const include: StaticDirectory = Transformer.reSealPartialDirectory(sdk, r`include/${version}/um`, "win");

    @@public
    export const lib: StaticDirectory = Transformer.reSealPartialDirectory(sdk, r`lib/${version}/um/${qualifier.platform}`, "win");

    @@public
    export const standardLibs: File[] = [
        ...addIfLazy(isWin, () => [
            lib.getFile(r`kernel32.lib`),
            lib.getFile(r`advapi32.lib`),
            lib.getFile(r`uuid.lib`),
            lib.getFile(r`ntdll.lib`)
        ])
    ];
}

namespace Shared {
    @@public
    export const include: StaticDirectory = Transformer.reSealPartialDirectory(sdk, r`include/${version}/shared`, "win");
}

namespace Ucrt {
    @@public
    export const include: StaticDirectory = Transformer.reSealPartialDirectory(sdk, r`include/${version}/ucrt`, "win");

    @@public
    export const lib: StaticDirectory = Transformer.reSealPartialDirectory(sdk, r`lib/${version}/ucrt/${qualifier.platform}`, "win");
}


function getHeadersAndLibs() : StaticDirectory {
    if (Environment.getFlag("[Sdk.BuildXL]microsoftInternal")) {
        // Internally in Microsoft we use a nuget package that contains the windows Sdk.
        return importFrom("Windows.Sdk").Contents.all;
    }

    const installedSdkLocation = d`${Context.getMount("ProgramFilesX86").path}/Windows Kits/10`;
    const windowsH = f`${installedSdkLocation}/Include/${version}/um/Windows.h`;
    if (!File.exists(windowsH))
    {
        Contract.fail(`Could not find the installed windows Sdk headers for version ${version}. File '${windowsH.toDiagnosticString()}' does not exist. Please install version ${version} from https://developer.microsoft.com/en-us/windows/downloads/sdk-archive. You don't need the full Sdk just the following features: 'Windows SDK for Desktop C++ x86 Apps', 'Windows SDK for Desktop C++ amd64 Apps' and its dependencies.`);
    }

    return Transformer.sealPartialDirectory(installedSdkLocation, globR(installedSdkLocation, "*.*"));
};