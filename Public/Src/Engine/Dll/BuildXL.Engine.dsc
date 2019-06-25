// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bond from "Sdk.SelfHost.Protocols.Bond";
import * as Managed from "Sdk.Managed";

namespace Engine {
    const useMicrosoftInternalBond = BuildXLSdk.Flags.isMicrosoftInternal && BuildXLSdk.isFullFramework && Context.getCurrentHost().os === "win";

    const bondOutput = useMicrosoftInternalBond ? Bond.generate({
        bondFile: f`Distribution/InternalBond/Distribution.bond`,
        templateType: "csharp",
    }) : undefined;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Engine",
        generateLogs: true,
        generateLogsLite: false,
        sources: [
            ...globR(d`.`, "*.cs"),
            ...addIfLazy(useMicrosoftInternalBond, () => [
                bondOutput.csharpResult.proxiesFile,
                bondOutput.csharpResult.servicesFile,
                bondOutput.csharpResult.typesFile,
            ]),
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
                importFrom("Bond.Rpc.NET").pkg,
                importFrom("Bond.Core.NET").pkg,
            ]),
            Cache.dll,
            Cache.Plugin.Core.dll,
            Processes.dll,
            Scheduler.dll,
            Distribution.Grpc.dll,
            importFrom("Bond.Core.CSharp").pkg,
            importFrom("Bond.Runtime.CSharp").pkg,
            importFrom("BuildXL.Cache.VerticalStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Ide").Generator.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("Google.Protobuf").pkg,
            importFrom("Grpc.Core").pkg,
            importFrom("Newtonsoft.Json").pkg,
        ],
        internalsVisibleTo: [
            "bxlScriptAnalyzer",
            "IntegrationTest.BuildXL.Scheduler",
            "Test.BuildXL.Engine",
            "Test.BuildXL.EngineTestUtilities",
            "Test.BuildXL.FrontEnd.Script",
            "Test.BuildXL.FrontEnd.Core",
            "Test.Tool.Analyzers",
        ],
    });
}
