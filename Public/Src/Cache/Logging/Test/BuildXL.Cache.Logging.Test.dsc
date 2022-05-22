// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.Logging.Test",
        sources: globR(d`.`,"*.cs"),
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            Library.dll,

            importFrom("WindowsAzure.Storage").pkg,
            importFrom("NLog").pkg,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.ContentStore").InterfacesTest.dll,
            importFrom("BuildXL.Cache.ContentStore").Test.dll,
            ...BuildXLSdk.bclAsyncPackages,
            
            ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal, () => [
                importFrom("Microsoft.Cloud.InstrumentationFramework").pkg,
                ]),

            ...BuildXLSdk.fluentAssertionsWorkaround,
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll
            ),
        ],
        runtimeContent: [
            Library.Deployment.runtimeContent,
        ],
        runTestArgs: {
            skipGroups: BuildXLSdk.isDotNetCoreBuild ? [ "SkipDotNetCore" ] : []
        }
    });
}
