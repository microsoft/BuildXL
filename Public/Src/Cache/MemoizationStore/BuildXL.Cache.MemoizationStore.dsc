// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as ContentStore from "BuildXL.Cache.ContentStore";

export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet451;

export {BuildXLSdk, ContentStore};

export const NetFx = BuildXLSdk.NetFx;

namespace Default {
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet451;

    @@public
    export const deployment: Deployment.Definition =
    {
        contents: [
            {
                subfolder: r`App`,
                contents: [
                    App.exe,
                    // Back-Compat naming
                    {
                        file: App.exe.runtime.binary,
                        targetFileName: "Microsoft.MemoizationStoreApp.exe",
                    },
                    {
                        file: App.exe.runtime.pdb,
                        targetFileName: "Microsoft.MemoizationStoreApp.pdb",
                    }
                ]
            },
            {
                subfolder: r`Distributed`,
                contents: [
                    Distributed.dll
                ]
            },
            {
                subfolder: r`Interfaces`,
                contents: [
                    Interfaces.dll
                ]
            },
            {
                subfolder: r`Library`,
                contents: [
                    Library.dll
                ]
            },
            {
                subfolder: r`VstsInterfaces`,
                contents: [
                    VstsInterfaces.dll
                ]
            },
            ...addIf(BuildXLSdk.Flags.isVstsArtifactsEnabled,
                {
                    subfolder: r`Vsts`,
                    contents: [
                        Vsts.dll
                    ]
                },
                {
                    subfolder: r`VstsApp`,
                    contents: [
                        VstsApp.dll
                    ]
                }
            ),
        ]
    };
}

@@public
export const deploymentForBuildXL: Deployment.Definition = BuildXLSdk.isDotNetCoreBuild ? undefined :  {
    contents: [
        App.exe,
        // Back-Compat naming
        {
            file: App.exe.runtime.binary,
            targetFileName: "Microsoft.MemoizationStoreApp.exe",
        },
        {
            file: App.exe.runtime.pdb,
            targetFileName: "Microsoft.MemoizationStoreApp.pdb",
        },
        importFrom("CLAP").Contents.all.getFile("lib/net35/CLAP.dll"),
    ]
};
