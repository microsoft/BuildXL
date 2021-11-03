// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import { NetFx } from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Branding from "BuildXL.Branding";
import * as Managed from "Sdk.Managed";

namespace LanguageService.Server {
    @@public
    export const artifact = BuildXLSdk.executable({
        embeddedResources: [{resX: f`Strings.resx`}],
        assemblyName: "BuildXL.Ide.LanguageServer",
        rootNamespace: "BuildXL.Ide.LanguageServer",
        appConfig: f`App.config`,
        tools: {
            csc: {
                noWarnings: [1591] // Missing XML comment 
            },
        },
        sources: [
            ...globR(d`.`, "*.cs"),
            ...addIf(qualifier.targetRuntime !== "win-x64",
                f`${Context.getMount("ThirdParty_mono").path}/mcs/class/Mono.Posix/Mono.Posix/UnixEndPoint.cs`
            )
        ],
        generateLogs: true,
        skipAssemblySigning: true,
        references: [
            Protocol.dll,
            IDE.Shared.JsonRpc.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Core.UnitTests").EngineTestUtilities.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Engine.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.FrontEnd").Factory.dll,
            importFrom("BuildXL.FrontEnd").Core.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.FrontEnd").TypeScript.Net.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Tools").BxlScriptAnalyzer.exe,
            importFrom("BuildXL.Tools").BxlPipGraphFragmentGenerator.exe,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("Microsoft.VisualStudio.Threading").pkg,
            importFrom("Microsoft.VisualStudio.Validation").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("StreamJsonRpc").pkg,
            importFrom("System.ComponentModel.Composition").pkg,
            importFrom("Microsoft.VisualStudio.LanguageServer.Protocol").pkg,
        ],
        runtimeReferences: [
            importFrom("Nerdbank.Streams").withQualifier({targetFramework:"netstandard2.1"}).pkg,
            importFrom("System.IO.Pipelines").pkg, 
            importFrom("System.Collections.Immutable.ForVBCS").pkg, 
        ],
        // StreamJsonRpc needs System.Collections.Immutable.dll v5.0.0, which is higher than the one provided
        // by the .net framework
        deploymentOptions: { ignoredSelfContainedRuntimeFilenames: [a`System.Collections.Immutable.dll`] }, 
    });

    @@public
    export const vsix = buildVsix(artifact);
}
