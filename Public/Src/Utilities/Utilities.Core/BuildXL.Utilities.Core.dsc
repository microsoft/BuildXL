// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Utilities.Core {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.Core",
        allowUnsafeBlocks: true,
        sources: [
            ...globR(d`.`, "*.cs"),
        ],
        addNotNullAttributeFile: true,
        references: [
            // IMPORTANT!!! Do not add non-bxl dependencies or any bxl projects with external dependencies into this project
            //              any non-bxl dependencies should go to BuildXL.Utilities instead

            ...addIfLazy(!BuildXLSdk.isDotNetCore, () => [
                NetFx.System.Xml.dll,
                NetFx.Netstandard.dll,
                importFrom("System.Threading.Channels").pkg,
                importFrom("System.Memory").pkg,
                importFrom("System.Threading.Tasks.Extensions").pkg,
            ]),
        ],
        internalsVisibleTo: [
            "BuildXL.FrontEnd.Script",
            "BuildXL.Pips",
            "BuildXL.Scheduler",
            "BuildXL.Utilities",
            "Test.BuildXL.Pips",
            "Test.BuildXL.Scheduler",
            "Test.BuildXL.Scheduler.EBPF",
            "Test.BuildXL.Utilities",
            "Test.BuildXL.Utilities.Collections",
            "Test.BuildXL.FrontEnd.Script",
            "IntegrationTest.BuildXL.Scheduler",
            "IntegrationTest.BuildXL.Scheduler.EBPF",
        ],
    });
}