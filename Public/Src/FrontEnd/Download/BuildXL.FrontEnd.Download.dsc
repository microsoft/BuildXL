// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Download {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Download",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Net.Http.dll,
                NetFx.System.Web.dll
            ),
            Core.dll,
            Script.dll,
            Sdk.dll,
            TypeScript.Net.dll,
            Utilities.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            ...BuildXLSdk.tplPackages,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.FrontEnd.Download",
        ],
        runtimeContent:[
            // We don't actually need to deploy the downloader for full framework
            // (it causes some deployment issues, and the full framework packages we generate don't need it anyway)
            ...addIfLazy(BuildXLSdk.isDotNetCoreApp,
                () => [importFrom("BuildXL.Tools").FileDownloader.withQualifier({
                    configuration : qualifier.configuration, 
                    targetFramework: qualifier.targetFramework,
                    targetRuntime: qualifier.targetRuntime }).deployment])
        ],
    });
}