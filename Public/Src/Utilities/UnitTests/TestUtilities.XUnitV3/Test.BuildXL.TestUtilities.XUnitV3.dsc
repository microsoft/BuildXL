// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TestUtilities.XUnitV3 {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "Test.BuildXL.TestUtilities.XUnitV3",
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                BuildXLSdk.NetFx.System.Reflection.dll,
                importFrom("Sdk.Managed.Frameworks.Net472").NetFx.Netstandard.dll,
                importFrom("Microsoft.Bcl.AsyncInterfaces").pkg,
                importFrom("System.Threading.Tasks.Extensions").pkg,
            ]),
            ...BuildXLSdk.getSystemMemoryPackagesWithoutNetStandard(),
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Processes.External.dll,
            importFrom("BuildXL.Pips").dll,
            ...importFrom("Sdk.Managed.Testing.XUnitV3").xunitV3References,
            TestUtilities.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
        ],
        defineConstants: BuildXLSdk.isDotNetCoreOrStandard ? ["DISABLE_FEATURE_XUNIT_PRETTYSTACKTRACE"] : []
    });
}
