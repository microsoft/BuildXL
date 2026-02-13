// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import { Transformer } from "Sdk.Transformers";

namespace Engine {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Engine",
        generateLogs: true,
        addNotNullAttributeFile: true,
        sources: [
            ...globR(d`.`, "*.cs"),
        ],
        embeddedResources: [
            {resX: f`Strings.resx`},
            {linkedContent: [f`Vhd/CreateSnapVhd.txt`, f`Vhd/DismountSnapVhd.txt`]}
        ],
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.IO.dll,
                NetFx.System.IO.Compression.dll,
                NetFx.System.ServiceProcess.dll,
                NetFx.System.IO.Compression.dll,
            ]),
            Cache.dll,
            Cache.Plugin.Core.dll,
            ProcessPipExecutor.dll,
            Processes.dll,
            Processes.External.dll,
            Scheduler.dll,
            Distribution.Grpc.dll,
            ViewModel.dll,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Grpc.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Ide").Generator.dll,
            importFrom("BuildXL.Ide").Generator.Old.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Native.Extensions.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcPackages(true),
            ...importFrom("BuildXL.Cache.ContentStore").getGrpcAspNetCorePackages(),
            importFrom("Newtonsoft.Json").pkg,
            importFrom("ZstdSharp.Port").pkg,
        ],
        internalsVisibleTo: [
            "bxlanalyzer",
            "BxlPipGraphFragmentGenerator",
            "IntegrationTest.BuildXL.Scheduler",
            "Test.BuildXL.Engine",
            "Test.BuildXL.Distribution",
            "Test.BuildXL.EngineTestUtilities",
            "Test.BuildXL.FrontEnd.Script",
            "Test.BuildXL.FrontEnd.Core",
            "Test.Tool.Analyzers",
            "BuildXL.FrontEnd.Script.Testing.Helper"
        ],
        runtimeContent: [
            // This explicitly does not include a check for the host OS despite being Linux only because this package can be built on Windows
            // but still used on Linux since it's a dotnet library.
            ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal, () => [
                {
                    subfolder: r`tools`,
                    contents: [
                        {
                            subfolder: r`dotnet-stack`,
                            contents: [
                                Transformer.reSealPartialDirectory(importFrom("dotnet-stack").pkg.contents, r`tools/net8.0/any/`)
                            ]
                        }
                    ]
                }
            ]),
        ]
    });
}
