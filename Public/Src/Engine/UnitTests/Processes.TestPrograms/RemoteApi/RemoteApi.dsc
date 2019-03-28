// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Native from "Sdk.Native";
import * as WindowsSdk from "WindowsSdk";
import * as VisualCpp from "VisualCpp";

namespace Processes.TestPrograms.RemoteApi {
    export declare const qualifier: BuildXLSdk.PlatformDependentQualifier;
    
    @@public
    export const exe = Native.Exe.build({
        outputFileName: PathAtom.create("RemoteApi.exe"),
        innerTemplates: {
            // Statically link the crt so we can run tests during the build on machines which don't have the debug crt installed.
            clRunner: {
                runtimeLibrary: qualifier.configuration === "debug" ? Native.Cl.RuntimeLibrary.multithreadedDebug : Native.Cl.RuntimeLibrary.multithreaded,
            },
        },
        sources: [f`Main.cpp`, f`stdafx.cpp`],
        includes: [
            f`stdafx.h`,
            f`Command.h`,
            importFrom("WindowsSdk").UM.include,
            importFrom("WindowsSdk").Shared.include,
            importFrom("WindowsSdk").Ucrt.include,
            importFrom("VisualCpp").include,
        ],
        libraries: [
            ...importFrom("WindowsSdk").UM.standardLibs,
            importFrom("VisualCpp").lib,
            importFrom("WindowsSdk").Ucrt.lib,
        ],         
    });
}
