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
        references: [
            importFrom("WindowsAzure.Storage").pkg,
            importFrom("NLog").pkg,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,

            ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal, () => [
                importFrom("microsoft.cloud.instrumentationframework.netstd").pkg,
                ]),
        ],
        internalsVisibleTo: [
            "BuildXL.Cache.Logging.Test"
        ],
        runtimeContent: BuildXLSdk.Flags.isMicrosoftInternal ? [Deployment.runtimeContent] : undefined,
    });

    namespace Deployment {

        export declare const qualifier : {targetRuntime: "win-x64" | "osx-x64" | "linux-x64"};

        const pkgContents = importFrom("microsoft.cloud.instrumentationframework.netstd").Contents.all;

        @@public
        export const runtimeContent: SdkDeployment.Definition = BuildXLSdk.Flags.isMicrosoftInternal ? {
            contents: [
                    Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/IfxEvents.dll`),
                    Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/IfxHealth.dll`),
                    Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/IfxMetrics.dll`),
                    Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/msvcp140.dll`),
                    Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/concrt140.dll`),
                    Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/Tfx.dll`),
                    Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/vccorlib140.dll`),
                    Managed.Factory.createBinary(pkgContents, r`runtimes/win-x64/native/vcruntime140.dll`)
            ]
        } : undefined;
    }
}
