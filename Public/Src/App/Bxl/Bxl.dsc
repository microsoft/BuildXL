// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";
import * as Branding from "BuildXL.Branding";

namespace Main {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "bxl",
        generateLogs: true,
        generateLogsLite: false,
        assemblyInfo: {
            fileVersion: Branding.Managed.fileVersion,
        },
        assemblyBindingRedirects: qualifier.targetFramework === 'net472' ? [
            {
                name: "System.Threading.Tasks.Dataflow",
                publicKeyToken: "b03f5f7f11d50a3a",
                culture: "neutral",
                oldVersion: "0.0.0.0-4.6.3.0",
                newVersion: "4.6.3.0",
            },
        ] : [],
        appConfig: f`App.Config`,
        sources: globR(d`.`, "*.cs"),
        embeddedResources: [{resX: f`Strings.resx`}],
        references: [
            ...(BuildXLSdk.isDotNetCoreBuild ? [
                importFrom("BuildXL.Cache.VerticalStore").BasicFilesystem.dll,
                importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
                importFrom("BuildXL.Cache.VerticalStore").InMemory.dll,
                importFrom("BuildXL.Cache.VerticalStore").MemoizationStoreAdapter.dll,
                importFrom("BuildXL.Cache.VerticalStore").VerticalAggregator.dll,
                ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [
                    importFrom("BuildXL.Cache.VerticalStore").BuildCacheAdapter.dll
                ]),

                // net461 of SQLite is compatible .netstandard 2.0
                // we use net461 because there is no .netstandard 2.0 nuget distribution
                importFrom("System.Data.SQLite.Core").withQualifier({targetFramework: "net461"}).pkg,
            ] : [
                NetFx.System.IO.Compression.dll,
                NetFx.System.IO.Compression.FileSystem.dll,
            ]),

            ...addIf(BuildXLSdk.Flags.isVstsArtifactsEnabled,
                importFrom("BuildXL.Cache.VerticalStore").BuildCacheAdapter.dll
            ),

            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Engine").VisualizationModel.dll,
            importFrom("BuildXL.Ide").Script.Debugger.dll,
            importFrom("BuildXL.Ide").Generator.dll,
            importFrom("BuildXL.Ide").VSCode.DebugProtocol.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Download.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Nuget.dll,
            importFrom("BuildXL.FrontEnd").MsBuild.dll,
            importFrom("BuildXL.FrontEnd").Ninja.dll,
            importFrom("BuildXL.FrontEnd").CMake.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
        internalsVisibleTo: [
            "IntegrationTest.BuildXL.Scheduler",
            "Test.Bxl",
        ],
    });
}
