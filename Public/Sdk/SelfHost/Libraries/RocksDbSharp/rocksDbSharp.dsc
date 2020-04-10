// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier: {
    targetFramework: "netcoreapp3.1" | "net462" | "net472" | "netstandard2.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
};

const nativePackage = importFrom("RocksDbNative").pkg;
const managedPackage = importFrom("RocksDbSharpSigned").pkg;

@@public
export const pkgs = [
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
];
