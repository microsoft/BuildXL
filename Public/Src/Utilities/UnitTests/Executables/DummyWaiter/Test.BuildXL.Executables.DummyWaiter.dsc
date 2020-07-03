// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DummyWaiter {

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "Test.BuildXL.Executables.DummyWaiter",
        sources: globR(d`.`, "*.cs")
    });
}
