// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VerifyFileContentTable {
    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "VerifyFileContentTable",
        rootNamespace: "Tool.VerifyFileContentTable",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.App").Main.exe,
        ],
        embeddedResources: [{resX: f`Properties/Resources.resx`}],
    });
}
