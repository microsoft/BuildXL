// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Tracing {

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Tracing",
        generateLogs: true,
        generateLogsLite: false,
        sources: [
            ...globR(d`.`, "*.cs"),
            importFrom("BuildXL.Tracing.AriaTenantToken").Contents.all.getFile(r`AriaTenantToken.cs`),
        ],
        skipDefaultReferences: true,
        references: [
            Common.dll,
            importFrom("BuildXL.Utilities").dll,
            ...(qualifier.targetFramework !== "net451" ? [] : [
                importFrom("BuildXL.Utilities").System.FormattableString.dll
            ]),
            importFrom("BuildXL.Utilities").Configuration.dll,
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                importFrom("Microsoft.Diagnostics.Tracing.TraceEvent").pkg,
                importFrom("Microsoft.Applications.Telemetry.Desktop").pkg,
                importFrom("Microsoft.Diagnostics.Tracing.EventSource.Redist").pkg,
            ]),
        ],
        embeddedResources: [{resX: f`Statistics.resx`, generatedClassMode: "implicitPublic"}],
    });
}
