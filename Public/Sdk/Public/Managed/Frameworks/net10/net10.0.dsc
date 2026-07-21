// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";
import * as Deployment from "Sdk.Deployment";
import * as MacOS from "Sdk.MacOS";
import {Helpers} from "Sdk.Managed.Frameworks";

export declare const qualifier: {targetFramework: "net10.0"};

const defaultAssemblies: Shared.Assembly[] = createDefaultAssemblies();

// NOTE: runtime.{rid}.Microsoft.NETCore.DotNetHostResolver / DotNetHostPolicy packages are still only
// published up to the 8.0.x series. We continue to use the .8.0 aliases here (same approach as net9).
// CODESYNC: config.nuget.dotnetcore.dsc
const windowsRuntimeFiles = [
    ...importFrom("Microsoft.NETCore.App.Runtime.win-x64.10.0").Contents.all.getContent().filter(f => f.extension === a`.dll`),
    ...importFrom("runtime.win-x64.Microsoft.NETCore.DotNetHostResolver.8.0").Contents.all.getContent().filter(f => f.extension === a`.dll`),
    ...importFrom("runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy.8.0").Contents.all.getContent().filter(f => f.extension === a`.dll`),
];

const osxRuntimeFiles = [
    ...importFrom("Microsoft.NETCore.App.Runtime.osx-x64.10.0").Contents.all.getContent().filter(f => Helpers.macOSRuntimeExtensions(f)),
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver.8.0").Contents.all.getContent().filter(f => Helpers.macOSRuntimeExtensions(f)),
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy.8.0").Contents.all.getContent().filter(f => Helpers.macOSRuntimeExtensions(f)),
];

const linuxRuntimeFiles = [
    ...importFrom("Microsoft.NETCore.App.Runtime.linux-x64.10.0").Contents.all.getContent().filter(f => Helpers.linuxRuntimeExtensions(f)),
    ...importFrom("runtime.linux-x64.Microsoft.NETCore.DotNetHostResolver.8.0").Contents.all.getContent().filter(f => Helpers.linuxRuntimeExtensions(f)),
    ...importFrom("runtime.linux-x64.Microsoft.NETCore.DotNetHostPolicy.8.0").Contents.all.getContent().filter(f => Helpers.linuxRuntimeExtensions(f)),
];


@@public
export function runtimeContentProvider(runtimeVersion: Shared.RuntimeVersion): File[] {
    switch (runtimeVersion)
    {
        case "osx-x64":
            return osxRuntimeFiles;
        case "win-x64":
            return windowsRuntimeFiles;
        case "linux-x64":
            return linuxRuntimeFiles;
        default:
            Contract.fail(`Unsupported runtime encountered: ${runtimeVersion}`);
    }
}

export function crossgenProvider(runtimeVersion: Shared.RuntimeVersion): Shared.CrossgenFiles {
    switch (runtimeVersion)
    {
        case "osx-x64":
            const osxFiles = importFrom("Microsoft.NETCore.App.Runtime.osx-x64.10.0").Contents.all;
            return { 
                crossgenExe: osxFiles.getFile(r`tools/crossgen`),
                JITPath: osxFiles.getFile(r`runtimes/osx-x64/native/libclrjit.dylib`)
            };
        case "win-x64":
            const winFiles = importFrom("Microsoft.NETCore.App.Runtime.win-x64.10.0").Contents.all;
            return {
                crossgenExe: winFiles.getFile(r`tools/crossgen.exe`),
                JITPath: winFiles.getFile(r`runtimes/win-x64/native/clrjit.dll`)
            };
        default:
            return undefined;
    }
}

@@public
export const framework : Shared.Framework = {
    targetFramework: qualifier.targetFramework,

    supportedRuntimeVersion: "v10.0",
    assemblyInfoTargetFramework: ".NETCoreApp,Version=v10.0",
    assemblyInfoFrameworkDisplayName: ".NET App",

    standardReferences: defaultAssemblies,

    requiresPortablePdb: true,

    runtimeConfigStyle: "runtimeJson",
    runtimeFrameworkName: "Microsoft.NETCore.App",
    runtimeConfigVersion: "10.0.10",

    // Deployment style for .NET Core applications currently defaults to self-contained
    defaultApplicationDeploymentStyle: "selfContained",
    runtimeContentProvider: runtimeContentProvider,
    crossgenProvider: crossgenProvider,

    conditionalCompileDefines: [
        "NET",
        "NETCOREAPP",
        "NETCOREAPP3_1_OR_GREATER",
        "NET5_0_OR_GREATER",
        "NET6_0",
        "NET6_0_OR_GREATER",
        "NET7_0",
        "NET7_0_OR_GREATER",
        "NET8_0",
        "NET8_0_OR_GREATER",
        "NET9_0",
        "NET9_0_OR_GREATER",
        "NET10_0",
        "NET10_0_OR_GREATER"
    ],
};

function createDefaultAssemblies() : Shared.Assembly[] {
    const pkgContents = importFrom("Microsoft.NETCore.App.Ref100").Contents.all;
    return Helpers.createDefaultAssemblies(pkgContents, "net10.0", /*includeAllAssemblies*/ true);
}
