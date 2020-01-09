// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Test.Tool.QTestMock {
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.QTestMock",
        sources: globR(d`.`, "*.cs"),
    });
}
