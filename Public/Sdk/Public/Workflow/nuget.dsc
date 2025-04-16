// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

/** Contains tasks for dealing with NuGet packages, e.g., restoring NuGet packages. */
namespace NuGet
{
    /** Arguments for restoring NuGet packages. */
    @@public
    export interface RestoreArguments
    {
        /** NuGet packages to restore. */
        packages: NuGetReference[];

        /** Target framework. */
        targetFramework: string;

        /** No warnings. */
        noWarns?: string[];

        /** Environment variables. */
        environmentVariables?: Transformer.EnvironmentVariable[];

        /** Root for package sources. */
        sourceRoot?: Directory;

        /** NuGet feeds. */
        feeds?: NuGetFeed[];

        /** Restore path. */
        restoreDirectory?: Directory;

        /** Extra dependencies. */
        dependencies?: TaskDependency[];

        /** Untracked scopes. */
        untrackedScopes?: Directory[];

        /** Untracked file or directory paths. */
        untrackedPaths?: (File | Directory)[];

        /** Description. */
        description?: string;
    }

    /** NuGet feed. */
    @@public
    export interface NuGetFeed
    {
        /** Name for the feed. */
        name: string;

        /** Location, e.g., URL. */
        location: string;
    }

    const assemblyNameForRestore = "Dummy__for__nuget__restore";

    @@public
    export const defaultUntrackedScopes : Directory[] = OS.isWindows
        ? [
            d`${Context.getMount("ProgramData").path}/Microsoft/VisualStudio`,
            d`${Context.getMount("ProgramData").path}/Microsoft Visual Studio`,
            d`${Context.getMount("LocalLow").path}/Microsoft/CryptnetUrlCache`,
            d`${Context.getMount("ProgramFiles").path}/Microsoft Visual Studio`,
            d`${Context.getMount("ProgramFilesX86").path}/Windows Kits/10`,
            d`${Context.getMount("ProgramFilesX86").path}/Microsoft SDKs`,
            d`${Context.getMount("ProgramFilesX86").path}/Reference Assemblies/Microsoft/Framework/.NETFramework`,
            d`${Context.getMount("UserProfile").path}/.nuget/plugins/netfx/CredentialProvider.Microsoft`,
            d`${Context.getMount("UserProfile").path}/.nuget/plugins/netcore/CredentialProvider.Microsoft`,
            d`${Context.getMount("UserProfile").path}/.nuget/v3-cache`,
            d`${Context.getMount("UserProfile").path}/.nuget/plugins-cache`,
            ...(Environment.hasVariable("NUGET_HTTP_CACHE_PATH") ? [Environment.getDirectoryValue("NUGET_HTTP_CACHE_PATH")] : []),
            ...(Environment.hasVariable("NUGET_PLUGINS_CACHE_PATH") ? [Environment.getDirectoryValue("NUGET_PLUGINS_CACHE_PATH")] : []),
            ...(Environment.hasVariable("NUGET_NETCORE_PLUGIN_PATHS") ? [d`${Environment.getDirectoryValue("NUGET_NETCORE_PLUGIN_PATHS").parent}`] : []),
            ...(Environment.hasVariable("NUGET_NETFX_PLUGIN_PATHS") ? [d`${Environment.getDirectoryValue("NUGET_NETFX_PLUGIN_PATHS").parent}`] : [])
          ]
        : [
            d`${Context.getMount("UserProfile").path}/.nuget`,
            d`${Context.getMount("UserProfile").path}/.local/share`,
          ];

    /** Restores NuGet packages. */
    @@public
    export function restore(args: RestoreArguments) : TaskOutput
    {
        if (args.packages.length === 0) return augmentTaskOutputWithReferences(noTask, args.dependencies);

        const workingDirectory = Context.getNewOutputDirectory("nugetRestore");
        const csProjFile = writeCsProjForRestore(args, workingDirectory);
        const nugetConfigFile = writeNuGetConfigForRestore(args, workingDirectory);
        const globalPackagesDirectory = Context.getNewOutputDirectory("nugetPackages");
        const restoreDirectory = args.restoreDirectory || Context.getNewOutputDirectory("nugetRestoreOutput");
        const restoreScriptFile = writeScriptForRestore(
            csProjFile,
            nugetConfigFile,
            globalPackagesDirectory,
            restoreDirectory,
            workingDirectory);
        const nugetSources = args.packages.mapMany(p => {
            if (p.directories !== undefined) 
                return p.directories.map(
                    d => typeof(d) === "Directory" 
                    ? Transformer.sealSourceDirectory(d, Transformer.SealSourceDirectoryOption.allDirectories)
                    : <StaticDirectory>d);

            const packageDir = getPackageSource(p, args.sourceRoot);
            return [ Transformer.sealSourceDirectory(packageDir, Transformer.SealSourceDirectoryOption.allDirectories) ];
        });

        const result = Transformer.execute({
            tool: {
                exe: OS.isWindows
                    ? Environment.getFileValue("COMSPEC")
                    : (Environment.hasVariable("SHELL") ? Environment.getFileValue("SHELL") : f`/bin/bash`),
                dependsOnCurrentHostOSDirectories: true,
                nestedTools: [DotNet.tool],
            },
            arguments: OS.isWindows
                ? [
                    Cmd.argument("/D"),
                    Cmd.argument("/C"),
                    Cmd.argument(Artifact.input(restoreScriptFile))
                  ]
                : [Cmd.argument(Artifact.input(restoreScriptFile))],
            workingDirectory: workingDirectory,
            dependencies: [
                nugetConfigFile,
                csProjFile,
                ...nugetSources,
                ...flattenTaskDependencies(true, false, ...(args.dependencies || []))
            ],
            outputs: [
                { kind: "shared", directory: restoreDirectory },
                globalPackagesDirectory,
                d`${workingDirectory.path}/obj`
            ],
            tempDirectory: Context.getTempDirectory(undefined),
            tags: ["nuget.restore"],
            environmentVariables: [
                ...(Env.removeTemp(args.environmentVariables) || []),
                { name: "UseSharedCompilation", value: "false" }
            ],
            enforceWeakFingerprintAugmentation: true,
            preservePathSetCasing: true,
            doubleWritePolicy: "allowSameContentDoubleWrites",
            sourceRewritePolicy: "safeSourceRewritesAreAllowed",
            allowUndeclaredSourceReads: true,
            unsafe: {
                requireGlobalDependencies: true,
                untrackedScopes: [...defaultUntrackedScopes, ...(args.untrackedScopes || [])],
                untrackedPaths: args.untrackedPaths
            },
            description: args.description
        });

        const taskOutput = { taskOutputs: [result.getOutputDirectory(restoreDirectory)] };
        return augmentTaskOutputWithReferences(taskOutput, args.dependencies);
    }

    function writeCsProjForRestore(args: RestoreArguments, outputDir: Directory) : DerivedFile
    {
        const noWarns = args.noWarns || [];
        const content = [
            "<Project Sdk=\"Microsoft.NET.Sdk\">",
            "<PropertyGroup>",
            "  <Version>1.0.0.0</Version>",
            "  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>",
            `  <TargetFramework>${args.targetFramework}</TargetFramework>`,
            `  <AssemblyName>${assemblyNameForRestore}</AssemblyName>`,
            `  <NoWarn>${noWarns.join(';')}</NoWarn>`,
            "</PropertyGroup>",
            "<ItemGroup>",
            ...args.packages.map(p => `  <PackageReference Include=\"${p.name}\" Version=\"${p.version}\"  />`),
            "</ItemGroup>",
          "</Project>"
        ];
        const csProjFileName = a`${assemblyNameForRestore}.csproj`;
        const csProjFile = p`${outputDir}/${csProjFileName}`;
        return Transformer.writeAllLines(csProjFile, content);
    }

    function writeNuGetConfigForRestore(args: RestoreArguments, outputDir: Directory) : DerivedFile
    {
        const producedPackages = args.packages.filter(p => p.directories !== undefined && p.directories.length > 0);
        const feeds = args.feeds || [];
        const content = [
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<configuration>",
            "  <packageRestore>",
            "    <add key=\"enabled\" value=\"True\" />",
            "    <add key=\"automatic\" value=\"True\" />",
            "  </packageRestore>",
            "  <packageSources>",
            "    <clear />",
            ...producedPackages.map(p => `    <add key=\"${p.name}\" value=\"${Debug.dumpData(getPackageSource(p, args.sourceRoot))}\" />`),
            ...feeds.map(f =>            `    <add key=\"${f.name}\" value=\"${f.location}\" />`),
            "  </packageSources>",
            "  <packageSourceMapping>",
            ...feeds.mapMany(f => [
                `    <packageSource key=\"${f.name}\">`,
                `      <package pattern=\"*\" />`,
                `    </packageSource>`
            ]),
            ...producedPackages.mapMany(p => [
                `    <packageSource key=\"${p.name}\">`,
                `      <package pattern=\"${p.name}\" />`,
                `    </packageSource>`
            ]),
            "  </packageSourceMapping>",
            "</configuration>"
        ];
        const nugetConfigFile = p`${outputDir}/nuget.config`;
        return Transformer.writeAllLines(nugetConfigFile, content);
    }

    function getPackageSource(package: NuGetReference, sourceRoot?: Directory) : Directory
    {
        if (package.source !== undefined) return package.source;

        if (sourceRoot === undefined) {
            if (package.directories !== undefined || package.directories.length > 0)
                sourceRoot = typeof(package.directories[0]) === "Directory" 
                    ? <Directory>package.directories[0]
                    : (<StaticDirectory>package.directories[0]).root;
            else
                sourceRoot = Context.getNewOutputDirectory(`nugetSource_${package.name}`);
        }

        return d`${sourceRoot}/${package.name}`;
    }

    function writeScriptForRestore(
        csProjFile: File,
        nugetConfigFile: File,
        globalPackagesDir: Directory,
        restoreDir: Directory,
        outputDir: Directory) : File
    {
        const props = [
            `RestoreConfigFile=${Debug.dumpData(nugetConfigFile.path)}`,
            "RestoreNoCache=true",
            `RestorePackagesPath=${Debug.dumpData(globalPackagesDir.path)}`,
            `OutputPath=${Debug.dumpData(restoreDir.path)}`
        ].join(';');

        const assemblyFileName = a`${assemblyNameForRestore}.dll`;
        const assemblyPath = p`${restoreDir.path}/${assemblyFileName}`;
        const assemblyPathStr = `\"${Debug.dumpData(assemblyPath)}\"`;

        const assemblyPdbFileName = a`${assemblyNameForRestore}.pdb`;
        const assemblyPdbPath = p`${restoreDir.path}/${assemblyPdbFileName}`;
        const assemblyPdbPathStr = `\"${Debug.dumpData(assemblyPdbPath)}\"`;

        const refPath = p`${restoreDir.path}/ref`;
        const refPathStr = `\"${Debug.dumpData(refPath)}\"`;
        const restoreScript = OS.isWindows
            ? [
                "@ECHO OFF",
                `\"${Debug.dumpData(DotNet.tool.exe.path)}\" msbuild \"${Debug.dumpData(csProjFile.path)}\" -restore -nologo -interactive:False -property:${props}`,
                `IF EXIST ${assemblyPathStr} DEL /F ${assemblyPathStr}`,
                `IF EXIST ${assemblyPdbPathStr} DEL /F ${assemblyPdbPathStr}`,
                `IF EXIST ${refPathStr} RMDIR /S /Q ${refPathStr}`,
                "IF %ERRORLEVEL% NEQ 0 (",
                "    EXIT /b %ERRORLEVEL%",
                ")",
                "EXIT /B 0"
              ]
            : [
                "#!/bin/bash",
                "set -eu",
                `\"${Debug.dumpData(DotNet.tool.exe.path)}\" msbuild \"${Debug.dumpData(csProjFile.path)}\" -restore -nologo -interactive:False -property:${props}`,
                "restoreExitCode=\"$?\"",
                `rm -f ${assemblyPathStr}`,
                `rm -f ${assemblyPdbPathStr}`,
                `rm -rf ${refPathStr}`,
                "return $restoreExitCode"
              ];

        const restoreScriptFile = Transformer.writeAllLines(p`${outputDir}/restore.cmd`, restoreScript);

        return OS.isWindows
            ? restoreScriptFile
            : Transformer.makeExecutable(restoreScriptFile, p`${outputDir}/restore.sh`);
    }
}