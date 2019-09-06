// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import { NetFx } from "Sdk.BuildXL";

export declare const qualifier : BuildXLSdk.FullFrameworkQualifier;

const symstoreX64Libs : Deployment.DeployableItem[] = getSymstoreX64Libs();

@@public
export const exe = !BuildXLSdk.isSymbolToolingEnabled ? undefined : BuildXLSdk.executable({
    assemblyName: "SymbolDaemon",
    rootNamespace: "Tool.SymbolDaemon",
    appConfig: f`SymbolDaemon.exe.config`,
    sources: globR(d`.`, "*.cs"),

    references: [
        importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
        importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
        importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
        importFrom("BuildXL.Utilities").dll,
        importFrom("BuildXL.Utilities").Ipc.dll,
        importFrom("BuildXL.Utilities").Native.dll,
        importFrom("BuildXL.Utilities").Storage.dll,
        importFrom("BuildXL.Tools").ServicePipDaemon.dll,

        importFrom("ArtifactServices.App.Shared").pkg,
        importFrom("ArtifactServices.App.Shared.Cache").pkg,        
        importFrom("Microsoft.ApplicationInsights").pkg,
        importFrom("Microsoft.AspNet.WebApi.Client").pkg,
        importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
        ...BuildXLSdk.visualStudioServicesArtifactServicesSharedPkg,
        importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg,
        importFrom("Microsoft.VisualStudio.Services.Client").pkg,
        importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
        importFrom("Newtonsoft.Json").pkg,

        importFrom("Symbol.App.Core").pkg,
        importFrom("Symbol.Client").pkg,
        importFrom("Microsoft.Windows.Debuggers.SymstoreInterop").pkg,
        importFrom("System.Reflection.Metadata").pkg,
    ],
    runtimeContent: symstoreX64Libs    
});

function getSymstoreX64Libs() : File[] {
    if (!BuildXLSdk.isSymbolToolingEnabled) {
        return undefined;
    }
    
    switch (qualifier.targetFramework)
    {       
        case "net472":
            return importFrom("Microsoft.Windows.Debuggers.SymstoreInterop").Contents.all.getFiles(
                [
                    "lib/native/x64/dbgcore.dll",
                    "lib/native/x64/dbghelp.dll",
                    "lib/native/x64/symsrv.dll",
                    "lib/native/x64/SymStore.dll",
                ]);
        default:
            Contract.fail("Unsupported target framework for x64 Symstore libraries.");
    }
}
