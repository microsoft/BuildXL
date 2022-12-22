// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";
import * as Deployment from "Sdk.Deployment";
import * as MacOS from "Sdk.MacOS";
import {Helpers} from "Sdk.Managed.Frameworks";

export declare const qualifier: {targetFramework: "net7.0"};

const defaultAssemblies: Shared.Assembly[] = createDefaultAssemblies();

const windowsRuntimeFiles = [
    // Don't need to exclude assemblies for net7 because they're higher versions.
    ...importFrom("Microsoft.NETCore.App.Runtime.win-x64.7.0").Contents.all.getContent().filter(f => f.extension === a`.dll`),
    ...importFrom("runtime.win-x64.Microsoft.NETCore.DotNetHostResolver.7.0").Contents.all.getContent().filter(f => f.extension === a`.dll`),
    ...importFrom("runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy.7.0").Contents.all.getContent().filter(f => f.extension === a`.dll`),
];

const osxRuntimeFiles = [
    ...importFrom("Microsoft.NETCore.App.Runtime.osx-x64.7.0").Contents.all.getContent().filter(f => Helpers.macOSRuntimeExtensions(f)),
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver.7.0").Contents.all.getContent().filter(f => Helpers.macOSRuntimeExtensions(f)),
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy.7.0").Contents.all.getContent().filter(f => Helpers.macOSRuntimeExtensions(f)),
];

const linuxRuntimeFiles = [
    ...importFrom("Microsoft.NETCore.App.Runtime.linux-x64.7.0").Contents.all.getContent().filter(f => Helpers.linuxRuntimeExtensions(f)),
    ...importFrom("runtime.linux-x64.Microsoft.NETCore.DotNetHostResolver.7.0").Contents.all.getContent().filter(f => Helpers.linuxRuntimeExtensions(f)),
    ...importFrom("runtime.linux-x64.Microsoft.NETCore.DotNetHostPolicy.7.0").Contents.all.getContent().filter(f => Helpers.linuxRuntimeExtensions(f)),
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
            const osxFiles = importFrom("Microsoft.NETCore.App.Runtime.osx-x64.7.0").Contents.all;
            return { 
                crossgenExe: osxFiles.getFile(r`tools/crossgen`),
                JITPath: osxFiles.getFile(r`runtimes/osx-x64/native/libclrjit.dylib`)
            };
        case "win-x64":
            const winFiles = importFrom("Microsoft.NETCore.App.Runtime.win-x64.7.0").Contents.all;
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

    supportedRuntimeVersion: "v7.0",
    assemblyInfoTargetFramework: ".NETCoreApp,Version=v7.0",
    assemblyInfoFrameworkDisplayName: ".NET App",

    standardReferences: defaultAssemblies,

    requiresPortablePdb: true,

    runtimeConfigStyle: "runtimeJson",
    runtimeFrameworkName: "Microsoft.NETCore.App",
    runtimeConfigVersion: "7.0.0-rc.1.22426.10",

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
        
        // Legacy symbols, not compatible with the official ones described here: https://docs.microsoft.com/en-us/dotnet/standard/frameworks
        "NET_CORE",
        "NET_COREAPP",
        "NET_COREAPP_60"
    ],
};

function createDefaultAssemblies() : Shared.Assembly[] {
    const pkgContents = importFrom("Microsoft.NETCore.App.Ref70").Contents.all;
    // Don't need to exclude assemblies for net7 because they're higher versions.
    return Helpers.createDefaultAssemblies(pkgContents, "net7.0", /*includeAllAssemblies*/ true);
}
