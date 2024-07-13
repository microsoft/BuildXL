// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as QTest from "Sdk.Managed.Testing.QTest";
import * as XUnit from "Sdk.Managed.Testing.XUnit";

namespace Test.Tool.QTestMock {

    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;

    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.QTestMock",
        sources: globR(d`.`, "*.cs"),
        testFramework: BuildXLSdk.Flags.isMicrosoftInternal
            ? QTest.getFramework(XUnit.framework)
            : undefined
    });
}
