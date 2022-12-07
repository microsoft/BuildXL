// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as SdkDeployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";

namespace Library {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472AndNetStandard20;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Cache.Logging",
        sources: globR(d`.`,"*.cs"),
        nullable: true,
        references: [
            BuildXLSdk.asyncInterfacesPackage,
            importFrom("WindowsAzure.Storage").pkg,
            importFrom("NLog").pkg,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.DistributedCache.Host").Configuration.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("System.Threading.Tasks.Extensions").pkg,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,

            ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal, () => [
                importFrom("Microsoft.Cloud.InstrumentationFramework").pkg,
                ]),
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Logging.Test"
        ],
        runtimeContent: BuildXLSdk.Flags.isMicrosoftInternal ? [Deployment.runtimeContent] : undefined,
    });

    namespace Deployment {

        export declare const qualifier : {targetRuntime: "win-x64" | "osx-x64" | "linux-x64"};

        const pkgContents = importFrom("Microsoft.Cloud.InstrumentationFramework").Contents.all;

        @@public
        export const runtimeContent: SdkDeployment.Definition = BuildXLSdk.Flags.isMicrosoftInternal ? {
            contents: [
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/concrt140.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/IfxEvents.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/IfxEvents.lib`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/IfxHealth.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/IfxHealth.lib`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/IfxMetrics.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/IfxMetrics.lib`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/msvcp140.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/msvcp140_1.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/msvcp140_2.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/Tfx.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/Tfx.lib`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/vccorlib140.dll`),
                Managed.Factory.createBinary(pkgContents, r`build/native/lib/x64/vcruntime140.dll`)
            ]
        } : undefined;
    }
}


