// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import {Transformer} from "Sdk.Transformers";

namespace Core {
    export declare const qualifier: BuildXLSdk.NetCoreAppQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        runTestArgs: {
            // These tests require Detours to run itself, so we won't detour the test runner process itself
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: true
            }
        },
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("Newtonsoft.Json").pkg,
            AsyncMutexClient.exe,
            ...BuildXLSdk.systemMemoryDeployment,
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, importFrom("BuildXL.Utilities").SBOMUtilities.dll),
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, importFrom("Microsoft.Sbom.Contracts").pkg),
            ...BuildXLSdk.fluentAssertionsWorkaround
        ],
        runtimeContent: [
            AsyncMutexClient.exe,
            ...addIf(Context.getCurrentHost().os === "win", 
                {
                    subfolder: r`git`,
                    contents: [importFrom("MinGit.win-x64").extracted],
                }
            ),
        ],

        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects()
    });
}
