// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";

export {BuildXLSdk};
export declare const qualifier: BuildXLSdk.DefaultQualifier;

namespace DetoursCrossBitTests
{
    export declare const qualifier: {
        configuration: "debug" | "release",
        targetRuntime: "win-x64",
    };

    @@public
    export const x64 = Processes.TestPrograms.DetoursCrossBitTests.withQualifier({
        platform: "x64",
        targetFramework: Managed.TargetFrameworks.DefaultTargetFramework,
        targetRuntime: "win-x64"
    }).exe;

    @@public
    export const x86 =Processes.TestPrograms.DetoursCrossBitTests.withQualifier({
        platform: "x86",
        targetFramework: Managed.TargetFrameworks.DefaultTargetFramework,
        targetRuntime: "win-x64"
    }).exe;
}
