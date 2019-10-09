// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TestUtilities.XUnit {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "Test.BuildXL.TestUtilities.XUnit",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Interop.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            ...importFrom("Sdk.Managed.Testing.XUnit").xunitReferences,
            TestUtilities.dll,
			importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
        ],
        defineConstants: BuildXLSdk.isDotNetCoreBuild ? ["DISABLE_FEATURE_XUNIT_PRETTYSTACKTRACE"] : []
    });
}
