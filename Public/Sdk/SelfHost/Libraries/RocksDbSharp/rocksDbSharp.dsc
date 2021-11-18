// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import {TargetFrameworks} from "Sdk.Managed.Shared";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier: {
    targetFramework: TargetFrameworks.AllFrameworks;
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
    configuration: "debug" | "release";
};

const nativePackage = importFrom("RocksDbNative").pkg;
const managedPackage = importFrom("RocksDbSharpSigned").pkg;

// This is meant to be used only when declaring NuGet packages' dependencies. In that particular case, you should be
// calling this function with includeNetStandard: false
@@public
export function getRocksDbPackages(includeNetStandard: boolean): Managed.ManagedNugetPackage[] {
    return [
        managedPackage.override<Managed.ManagedNugetPackage>({
            // Rename the package so that we declare the proper nuget dependency.
            name: "RocksDbSharp",
        }),
    
        nativePackage.override<Managed.ManagedNugetPackage>({
            // Mimic the custom msbuild targets to copy bits.
            runtimeContent: {
                contents: [ <Deployment.NestedDefinition>{
                    subfolder: r`native`,
                    contents: [ Deployment.createFromFilteredStaticDirectory(nativePackage.contents, r`build/native`) ] }
                ]
            }
        }),

        ...BuildXLSdk.getSystemMemoryPackages(includeNetStandard),
    ];
}

@@public
export const pkgs = getRocksDbPackages(true);