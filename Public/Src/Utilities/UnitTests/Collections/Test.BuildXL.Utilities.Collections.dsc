// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as QTest from "Sdk.Managed.Testing.QTest";
import * as XUnit from "Sdk.Managed.Testing.XUnit";

namespace Collections {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities.Collections",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
        testFramework: BuildXLSdk.Flags.isMicrosoftInternal
            ? QTest.getFramework(XUnit.framework)
            : undefined
    });
}
