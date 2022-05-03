// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";
import * as Deployment from "Sdk.Deployment";
import * as MacOS from "Sdk.MacOS";

export declare const qualifier: {targetFramework: "netcoreapp3.1"};

const defaultAssemblies: Shared.Assembly[] = createDefaultAssemblies();

function macOSRuntimeExtensions(file: File): boolean {
    return file.extension === a`.dylib` || file.extension === a`.a` || file.extension === a`.dll`;
}

function linuxRuntimeExtensions(file: File): boolean {
    return file.extension === a`.so` || file.extension === a`.o` || file.extension === a`.dll`;
}

function ignoredAssembly(file: File): boolean {
    // We skip deploying those files from the .NET Core package as we need those very assemblies from their dedicated package
    // to compile our platform abstraction layer, which depends on datatypes present only in the dedicated packages
    return file.name === a`System.IO.FileSystem.AccessControl.dll` ||
           file.name === a`System.Security.AccessControl.dll` ||
           file.name === a`System.Security.Principal.Windows.dll`;
}

const windowsRuntimeFiles = [
    ...importFrom("Microsoft.NETCore.App.Runtime.win-x64").Contents.all.getContent().filter(f => f.extension === a`.dll` && !ignoredAssembly(f)),
    ...importFrom("runtime.win-x64.Microsoft.NETCore.DotNetHostResolver").Contents.all.getContent().filter(f => f.extension === a`.dll`),
    ...importFrom("runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy").Contents.all.getContent().filter(f => f.extension === a`.dll`),
];

const osxRuntimeFiles = [
    ...importFrom("Microsoft.NETCore.App.Runtime.osx-x64").Contents.all.getContent().filter(f => macOSRuntimeExtensions(f) && !ignoredAssembly(f)),
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver").Contents.all.getContent().filter(f => macOSRuntimeExtensions(f)),
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy").Contents.all.getContent().filter(f => macOSRuntimeExtensions(f)),
];

const linuxRuntimeFiles = [
    ...importFrom("Microsoft.NETCore.App.Runtime.linux-x64").Contents.all.getContent().filter(f => linuxRuntimeExtensions(f) && !ignoredAssembly(f)),
    ...importFrom("runtime.linux-x64.Microsoft.NETCore.DotNetHostResolver").Contents.all.getContent().filter(f => linuxRuntimeExtensions(f)),
    ...importFrom("runtime.linux-x64.Microsoft.NETCore.DotNetHostPolicy").Contents.all.getContent().filter(f => linuxRuntimeExtensions(f)),
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
            const osxFiles = importFrom("Microsoft.NETCore.App.Runtime.osx-x64").Contents.all;
            return { 
                crossgenExe: osxFiles.getFile(r`tools/crossgen`),
                JITPath: osxFiles.getFile(r`runtimes/osx-x64/native/libclrjit.dylib`)
            };
        case "win-x64":
            const winFiles = importFrom("Microsoft.NETCore.App.Runtime.win-x64").Contents.all;
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

    supportedRuntimeVersion: "v3.1",
    assemblyInfoTargetFramework: ".NETCoreApp,Version=v3.1",
    assemblyInfoFrameworkDisplayName: ".NET Core App",

    standardReferences: defaultAssemblies,

    requiresPortablePdb: true,

    runtimeConfigStyle: "runtimeJson",
    runtimeFrameworkName: "Microsoft.NETCore.App",
    runtimeConfigVersion: "3.1.0",

    // Deployment style for .NET Core applications currently defaults to self-contained
    defaultApplicationDeploymentStyle: "selfContained",
    runtimeContentProvider: runtimeContentProvider,
    crossgenProvider: crossgenProvider,

    conditionalCompileDefines: [
        "NET",
        "NETCOREAPP",
        "NETCOREAPP3_1_OR_GREATER",
        
        // Legacy symbols, not compatible with the official ones described here: https://docs.microsoft.com/en-us/dotnet/standard/frameworks
        "NET_CORE",
        "NET_COREAPP",
        "NET_COREAPP_31",
    ],
};

function createDefaultAssemblies() : Shared.Assembly[] {
    const pkgContents = importFrom("Microsoft.NETCore.App.Ref").Contents.all;
    const netcoreAppPackageContents = pkgContents.contents;
    const dlls = netcoreAppPackageContents.filter(file => file.hasExtension && file.extension === a`.dll`);
    return dlls.map(file  => Shared.Factory.createAssembly(pkgContents, file, "netcoreapp3.1", [], true));
}
