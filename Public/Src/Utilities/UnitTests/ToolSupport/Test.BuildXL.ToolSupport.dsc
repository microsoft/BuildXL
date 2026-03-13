// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as XUnitV3 from "Sdk.Managed.Testing.XUnitV3";

namespace ToolSupport {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.ToolSupport",
        sources: globR(d`.`, "*.cs"),
        testFramework: XUnitV3.framework,
        references: [
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
        ],
    });
}
