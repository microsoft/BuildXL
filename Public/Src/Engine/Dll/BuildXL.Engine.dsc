// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import { Transformer } from "Sdk.Transformers";

namespace Engine {
    // Engine.dll is only used by bxl.exe (net8+). No net472 needed.
    export declare const qualifier: BuildXLSdk.DefaultQualifier;

    /**
     * The dotnet-gcdump and dotnet-stack tool packages carry native Windows dependencies (the TraceEvent ETW
     * and DIA libraries under amd64/, x86/ and arm64/) as well as a native launcher shim per RID under shims/.
     * BuildXL always invokes these tools as 'dotnet <tool>.dll' (see EngineDumpCollector.TryCaptureEngineDump
     * and ProcessDumper.TryDumpManagedStacksLinux), so the shims are never used, and the native dependencies
     * can only be loaded on Windows. Strip both when the target runtime is not win-x64 to keep roughly 29MB of
     * unusable binaries out of the linux-x64 and osx-x64 deployments.
     */
    function sealDiagnosticTool(pkgContents: StaticDirectory) : StaticDirectory {
        const root = d`${pkgContents.root}/tools/net8.0/any`;
        const windowsOnlyFolders = [ a`amd64`, a`x86`, a`arm64`, a`shims` ];
        return Transformer.sealPartialDirectory(
            root,
            pkgContents.contents.filter(f =>
                f.isWithin(root) &&
                (BuildXLSdk.isTargetRuntimeWin || !windowsOnlyFolders.some(folder => f.isWithin(d`${root}/${folder}`)))));
    }

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
            importFrom("System.IO.Hashing").pkg,
        ],
        internalsVisibleTo: [
            "bxlanalyzer",
            "BxlPipGraphFragmentGenerator",
            "IntegrationTest.BuildXL.Scheduler",
            "Test.BuildXL.Engine",
            "Test.BuildXL.Distribution",
            "Test.BuildXL.Distribution.Benchmarks",
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
                            // CodeSync: path must match EngineDumpCollector.TryCaptureEngineDump (tools/dotnet-gcdump)
                            subfolder: r`dotnet-gcdump`,
                            contents: [
                                sealDiagnosticTool(importFrom("dotnet-gcdump").pkg.contents)
                            ]
                        },
                        {
                            subfolder: r`dotnet-stack`,
                            contents: [
                                sealDiagnosticTool(importFrom("dotnet-stack").pkg.contents)
                            ]
                        }
                    ]
                }
            ]),
        ]
    });
}
