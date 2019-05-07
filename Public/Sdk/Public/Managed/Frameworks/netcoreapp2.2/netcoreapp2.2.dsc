// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";
import * as Deployment from "Sdk.Deployment";
import * as MacOS from "Sdk.MacOS";

export declare const qualifier: {targetFramework: "netcoreapp2.2"};

const defaultAssemblies: Shared.Assembly[] = createDefaultAssemblies();

function macOSRuntimeExtensions(file: File): boolean {
    return file.extension === a`.dylib` || file.extension === a`.a` || file.extension === a`.dll`;
}

function ignoredAssembly(file: File): boolean {
    let f = file.name;
    return f.equals(a`System.Security.AccessControl.dll`)
    || f.equals(a`System.Security.Principal.Windows.dll`);
}

const windowsRuntimeFiles = [
    ...importFrom("runtime.win-x64.Microsoft.NETCore.App").Contents.all.getContent().filter(f => f.extension === a`.dll` && !ignoredAssembly(f)),
    ...importFrom("runtime.win-x64.Microsoft.NETCore.DotNetHostResolver").Contents.all.getContent().filter(f => f.extension === a`.dll`),
    ...importFrom("runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy").Contents.all.getContent().filter(f => f.extension === a`.dll`),
];

const osxRuntimeFiles = [
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.App").Contents.all.getContent().filter(f => macOSRuntimeExtensions(f) && !ignoredAssembly(f)),
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver").Contents.all.getContent().filter(f => macOSRuntimeExtensions(f)),
    ...importFrom("runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy").Contents.all.getContent().filter(f => macOSRuntimeExtensions(f)),
];

@@public
export function runtimeContentProvider(runtimeVersion: Shared.RuntimeVersion): File[] {
    switch (runtimeVersion)
    {
        case "osx-x64":
            return osxRuntimeFiles;
        case "win-x64":
        default:
            return windowsRuntimeFiles;
    }
}

@@public
export const framework : Shared.Framework = {
    targetFramework: qualifier.targetFramework,

    supportedRuntimeVersion: "v2.2",
    assemblyInfoTargetFramework: ".NETCoreApp,Version=v2.2",
    assemblyInfoFrameworkDisplayName: ".NET Core App",

    standardReferences: defaultAssemblies,

    requiresPortablePdb: true,

    runtimeConfigStyle: "runtimeJson",
    runtimeFrameworkName: "Microsoft.NETCore.App",
    runtimeConfigVersion: "2.2.0",

    // Deployment style for .NET Core applications currently defaults to self-contained
    applicationDeploymentStyle: "selfContained",
    runtimeContentProvider: runtimeContentProvider,
};

function createDefaultAssemblies() : Shared.Assembly[] {
    const pkgContents = importFrom("Microsoft.NETCore.App").withQualifier({targetFramework: "netcoreapp2.2"}).Contents.all;
    const netcoreAppPackageContents = pkgContents.contents;
    const dlls = netcoreAppPackageContents.filter(file => file.hasExtension && file.extension === a`.dll`);
    return dlls.map(file  => Shared.Factory.createAssembly(pkgContents, file, "netcoreapp2.2", [], true));
}

function getToolTemplate() : Transformer.ExecuteArgumentsComposible {
    const host = Context.getCurrentHost();

    Contract.assert(host.cpuArchitecture === "x64", "The current DotNetCore Runtime package only has x64 version of Node. Ensure this runs on a 64-bit OS -or- update PowerShell.Core package to have other architectures embedded and fix this logic");

    let executable : RelativePath = undefined;
    let pkgContents : StaticDirectory = undefined;

    switch (host.os) {
        case "win":
            pkgContents = importFrom("DotNet-Runtime.win-x64").extracted;
            executable = r`dotnet.exe`;
            break;
        case "macOS":
            pkgContents = importFrom("DotNet-Runtime.osx-x64").extracted;
            executable = r`dotnet`;
            break;
        case "unix":
            pkgContents = importFrom("DotNet-Runtime.linux-x64").extracted;
            executable = r`dotnet`;
            break;
        default:
            Contract.fail(`The current DotNetCore Runtime package doesn't support the current target runtime: ${host.os}. Esure you run on a supported OS -or- update the DotNet-Runtime package to have the version embdded.`);
    }

    return {
        tool: {
            exe: pkgContents.getFile(executable),
            dependsOnCurrentHostOSDirectories: true,
        },
        dependencies: [
            pkgContents,
        ]
    };
}

const toolTemplate = getToolTemplate();

@@public
export function wrapInDotNetExeForCurrentOs(args: Transformer.ExecuteArguments) : Transformer.ExecuteArguments {
    return Object.merge<Transformer.ExecuteArguments>(
        args,
        toolTemplate,
        {
            arguments: [
                Cmd.argument(Artifact.input(args.tool.exe))
            ].prependWhenMerged()
        });
}
