// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * Contains tool definition for .NET tool.
 *
 * These namespace also include runtime directory dependencies and default untracked scopes, that are
 * useful for tasks that indirectly call .NET tool.
 *
 * TODO: Merge this namespace with DotNetCore.DotNetCoreRunner module and use the latter module.
 *       Currently, for easy customer onboarding, we want to make Sdk.Workflow self-contained.
 */
namespace DotNet
{
    const dotnetInstallPath = Environment.hasVariable("DOTNET_INSTALL_DIR")
        ? Environment.getDirectoryValue("DOTNET_INSTALL_DIR")
        : (OS.isWindows 
            ? d`${Context.getMount("ProgramFiles").path}/dotnet`
            : d`/usr/share/dotnet`); // Ubuntu default installation.

    const exe = OS.isWindows ? r`dotnet.exe` : r`dotnet`;

    /** Runtime directory dependencies. */
    @@public
    export const runtimeDirectory = Transformer.sealSourceDirectory(dotnetInstallPath, Transformer.SealSourceDirectoryOption.allDirectories);

    /** Default untracked scopes for .NET tool. */
    @@public
    export const defaultUntrackedScopes =  [
        d`${Context.getMount("UserProfile").path}/.dotnet`,
        ...addIfLazy(OS.isWindows, () => [
            d`${Context.getMount("ProgramData").path}/microsoft/netFramework/breadcrumbStore`,
            d`${Context.getMount("LocalLow").path}/Microsoft/CryptnetUrlCache`,
            d`${Context.getMount("ProgramFiles").path}/PowerShell/7`
        ]),
        ...addIfLazy(!OS.isWindows, () => [
            d`/etc`,
            d`/init`,
            d`/mnt`
        ]),
    ];

    /** .NET tool definition. */
    @@public
    export const tool: Transformer.ToolDefinition = {
        exe: f`${runtimeDirectory.path}/${exe}`,
        description: "dotnet.exe",
        runtimeDirectoryDependencies: [runtimeDirectory],
        dependsOnCurrentHostOSDirectories: true,
        dependsOnAppDataDirectory: true,
        prepareTempDirectory: true,
        untrackedDirectoryScopes: defaultUntrackedScopes
    };
}