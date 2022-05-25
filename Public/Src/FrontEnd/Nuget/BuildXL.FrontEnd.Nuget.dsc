// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

import * as Managed from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";

namespace Nuget {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Nuget",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.Netstandard.dll,
                NetFx.System.IO.Compression.dll,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
                NetFx.System.Net.Http.dll
            ),
            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("System.Memory").withQualifier({targetFramework: "netstandard2.0"}).pkg
            ),

            Sdk.dll,
            Core.dll,
            Script.dll,
            TypeScript.Net.dll,

            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").VstsAuthentication.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,

            importFrom("Newtonsoft.Json").pkg,
            importFrom("NuGet.Versioning").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Protocol").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Configuration").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Common").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Frameworks").withQualifier({targetFramework: "netstandard2.0"}).pkg,
            importFrom("NuGet.Packaging").withQualifier({targetFramework: "netstandard2.0"}).pkg,

            ...BuildXLSdk.tplPackages,
        ],
        runtimeContent: [
            // Keep in sync with path at Public\Sdk\Public\Tools\NugetDownloader\Tool.NugetDownloader.dsc
            importFrom("BuildXL.Tools").NugetDownloader.dll
        ],
        internalsVisibleTo: [
            "Test.BuildXL.FrontEnd.Nuget"
        ],
    });
}
