// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Tracing {
    export declare const qualifier: BuildXLSdk.AllSupportedQualifiers;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Tracing",
        nullable: true,
        generateLogs: true,
        generateLogBinaryRefs: [
            Common.dll.compile,
            importFrom("BuildXL.Utilities").Configuration.dll.compile,
        ],
        sources: [
            ...globR(d`.`, "*.cs"),
            importFrom("BuildXL.Tracing.AriaTenantToken").Contents.all.getFile(r`AriaTenantToken.cs`),
        ],
        skipDefaultReferences: true,
        references: [
            Common.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll
        ],
        embeddedResources: [{resX: f`Statistics.resx`, generatedClassMode: "implicitPublic"}],
    });
}
