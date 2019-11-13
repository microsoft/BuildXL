// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Branding from "BuildXL.Branding";

@@public
export const dll = BuildXLSdk.library({
    assemblyName: "BuildXL.Utilities",
    allowUnsafeBlocks: true,
    embeddedResources: [{resX: f`Strings.resx`, generatedClassMode: "implicitPublic"}],
    sources: globR(d`.`, "*.cs"), 
    references: [
        ...addIf(BuildXLSdk.isFullFramework,
            NetFx.System.Xml.dll,
            NetFx.System.Xml.Linq.dll,
            NetFx.System.Management.dll,
            NetFx.System.Security.dll
        ),
        Collections.dll,
        Interop.dll,
        importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
        ...addIfLazy(BuildXLSdk.isDotNetCoreBuild, () => [
            importFrom("Microsoft.Win32.Registry").pkg,
            importFrom("System.Security.Cryptography.ProtectedData").pkg
        ]),
        ...BuildXLSdk.tplPackages,
        importFrom("Newtonsoft.Json").pkg,
    ],
    defineConstants: qualifier.configuration === "debug" ? ["DebugStringTable"] : [],
    internalsVisibleTo: [
        "BuildXL.FrontEnd.Script",
        "BuildXL.Pips",
        "BuildXL.Scheduler",
        "Test.BuildXL.Pips",
        "Test.BuildXL.Scheduler",
        "Test.BuildXL.Utilities",
        "Test.BuildXL.FrontEnd.Script",
    ],
});
