// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace StorageTestUtilities {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "Test.BuildXL.StorageTestUtilities",
        sources: globR(d`.`, "*.cs"),
        addNotNullAttributeFile: true,
        references: [
            TestUtilities.dll,
            TestUtilities.XUnitV3.dll,
            ...importFrom("Sdk.Managed.Testing.XUnitV3").xunitV3References,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });
}
