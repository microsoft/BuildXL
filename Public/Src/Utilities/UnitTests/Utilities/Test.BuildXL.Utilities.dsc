// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import {Transformer} from "Sdk.Transformers";
import * as XUnit from "Sdk.Managed.Testing.XUnit";

namespace Core {
    export declare const qualifier: BuildXLSdk.Net6PlusQualifier;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities",
        allowUnsafeBlocks: true,
        // The SBOM utilities have their own dll since it cannot be a net6.0 due to latest SBOM packages not supporting net6.0 anymore.
        // TODO: merge SBOM utilities back to this dll once we stop building for net6.0. 
        // CODESYNC: Public\Src\Utilities\UnitTests\Utilities\SBOM\Test.BuildXL.Utilities.SBOM.dsc
        sources: globR(d`.`, "*.cs").filter(f => !f.isWithin(d`SBOM`)),
        // TODO - there is some issue with deploying the git binaries to the unit test directory under QTest.
        // Leave this as xunit for now
        testFramework: XUnit.framework,
        runTestArgs: {
            unsafeTestRunArguments: {
                untrackedScopes: [
                     ...addIfLazy(Context.getCurrentHost().os === "win", () => [
                           d`${Context.getMount("ProgramFiles").path}/Git/etc/gitconfig`,
                           d`${Context.getMount("SourceRoot").path}/.git`,
                     ]),
                     ...addIfLazy(!Context.isWindowsOS(), () => [d`/tmp/.dotnet/shm`])
                ]
            },
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
