// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Service {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Host.Service",
        sources: globR(d`.`,"*.cs"),
        skipDocumentationGeneration: true,
        references: [
            Configuration.dll,
            ...importFrom("BuildXL.Cache.ContentStore").getSerializationPackages(true),

            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").Distributed.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,

            importFrom("BuildXL.Cache.MemoizationStore").Library.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Distributed.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,

            BuildXLSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),

            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,

            importFrom("BuildXL.Cache.Roxis").Client.dll,

            importFrom("BuildXL.Cache.Logging").Library.dll,
            importFrom("WindowsAzure.Storage").pkg,
            importFrom("NLog").pkg,

            ...BuildXLSdk.bclAsyncPackages,

            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Web.dll,
                NetFx.System.Net.Http.dll,
                NetFx.System.Xml.dll
            ),
        ],
        allowUnsafeBlocks: false,
        internalsVisibleTo: [
            "BuildXL.Cache.Host.Test",
        ]
    });
}
