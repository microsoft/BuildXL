// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";

namespace Distributed {
    @@public
    export const eventHubPackagages = [
        // Event hub packages does not support 4.5.1 and for net472 we also have to reference the package with net461 qualifier
        // to keep all the transitive dependencies of the package.
        (qualifier.targetFramework === 'net472' || qualifier.targetFramework === 'net451') ? importFrom("Microsoft.Azure.EventHubs").withQualifier({targetFramework: 'net461'}).pkg : importFrom("Microsoft.Azure.EventHubs").pkg,
        // Microsoft.Azure.EventHubs removes 'System.Diagnostics.DiagnosticSource' as the dependency to avoid deployment issue for .netstandard2.0, but this dependency
        // is required for non-.net core builds.
        ...((qualifier.targetFramework === 'net472' || qualifier.targetFramework === 'net451' || qualifier.targetFramework === 'net461')
            ? [ importFrom("System.Diagnostics.DiagnosticSource").pkg, importFrom("Microsoft.IdentityModel.Tokens").pkg,importFrom("Microsoft.IdentityModel.Logging").pkg ] 
            : []),

    ];

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.ContentStore.Distributed",
        sources: globR(d`.`,"*.cs"),
        embeddedResources: [
            {
                linkedContent: glob(d`Redis/Scripts`,"*.lua"),
            },
        ],
        references: [
            ...eventHubPackagages,
            // Intentionally using different Azure storage package
            importFrom("WindowsAzure.Storage").pkg,
            importFrom("StackExchange.Redis.StrongName").pkg,
            ManagedSdk.Factory.createBinary(importFrom("TransientFaultHandling.Core").Contents.all, r`lib/NET4/Microsoft.Practices.TransientFaultHandling.Core.dll`),
            UtilitiesCore.dll,
            Hashing.dll,
            Interfaces.dll,
            Library.dll,
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll,
                NetFx.System.IO.Compression.dll,
                NetFx.System.IO.Compression.FileSystem.dll,
                NetFx.System.Net.Http.dll,
                NetFx.System.Web.dll
            ),
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Branding.dll,
            importFrom("BuildXL.Utilities").KeyValueStore.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("Sdk.Selfhost.RocksDbSharp").pkg,
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.ContentStore.Distributed.Test",
            "BuildXL.Cache.ContentStore.Distributed.Test.LongRunning",
            "BuildXL.Cache.MemoizationStore.Distributed",
            "BuildXL.Cache.MemoizationStore.Distributed.Test",
            "BuildXL.Cache.MemoizationStore.Vsts.Test",
        ]
    });
}
