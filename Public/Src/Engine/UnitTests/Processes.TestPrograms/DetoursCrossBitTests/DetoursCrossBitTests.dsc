// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Processes.TestPrograms.DetoursCrossBitTests {
    interface PlatformSpecificManagedCode extends Qualifier, BuildXLSdk.DefaultQualifier
    {
        platform: "x86" | "x64";
    }

    export declare const qualifier: PlatformSpecificManagedCode;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "DetoursCrossBitTests-" + qualifier.platform,
        platform: qualifier.platform,
        skipDocumentationGeneration: true,
        sources: [f`Program.cs`],
        references: [
            Scheduler.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
        ],
    });
}
