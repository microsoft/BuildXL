// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";

const isMacOS = Context.getCurrentHost().os === "macOS";

const pkgContents = isMacOS
    ? importFrom("runtime.osx-x64.Microsoft.DotNet.ILCompiler").Contents.all
    : importFrom("runtime.win-x64.Microsoft.DotNet.ILCompiler").Contents.all;

const netcoreAppPkgContents = isMacOS
    ? importFrom("runtime.osx-x64.Microsoft.NETCore.App").Contents.all
    : importFrom("runtime.win-x64.Microsoft.NETCore.App").Contents.all;

const ilcToolPackagePath = isMacOS
    ? r`tools/ilc`
    : r`tools/ilc.exe`;

const ilcTool: Transformer.ToolDefinition = Shared.Factory.createTool({
    exe: pkgContents.getFile(ilcToolPackagePath),
    runtimeDependencies: pkgContents.contents,
    dependsOnCurrentHostOSDirectories: true
});

@@public
export const linkTimeLibraries: File[] =
    pkgContents.getFiles(isMacOS ? [
        r`sdk/libbootstrapper.a`,
        r`sdk/libRuntime.a`,
        r`sdk/libSystem.Private.CoreLib.Native.a`,
        r`sdk/libSystem.Private.TypeLoader.Native.a`,
        r`framework/System.Native.a`,
        r`framework/System.Globalization.Native.a`,
        r`framework/System.IO.Compression.Native.a`,
        r`framework/System.Net.Http.Native.a`,
        r`framework/System.Net.Security.Native.a`,
        r`framework/System.Security.Cryptography.Native.Apple.a`
    ] : [
        // TODO: libraries for Windows
    ]);

@@public
export const compileTimeReferences: File[] = [
    ...pkgContents.contents.filter(f => f.name.extension === a`.dll` && f.path.parent.name !== a`tools`),
    ...(isMacOS ? netcoreAppPkgContents.getFiles([
        r`runtimes/osx-x64/lib/netcoreapp3.0/SOS.NETCore.dll`,
        r`runtimes/osx-x64/lib/netcoreapp3.0/System.Runtime.InteropServices.WindowsRuntime.dll`
    ]) : [
        // TODO: references for Windows
    ])
];

@@public
export const defaultArgs = <Arguments>{
    out: undefined,
    inputs: undefined,
    initAssemblies: [
        "System.Private.CoreLib",
        "System.Private.DeveloperExperience.Console",
        "System.Private.StackTraceMetadata",
        "System.Private.TypeLoader",
        "System.Private.Reflection.Execution",
        "System.Private.Interop"
    ],
    references: compileTimeReferences,
    stackTraceData: true,
    scanReflection: true,
    emitDebugInformation: true,
    completeTypeMetadata: true,
    rootAllApplicationAssemblies: true,
    dependencies: [
        pkgContents
    ]
};

@@public
export interface Arguments extends Transformer.RunnerArguments {
    /** Input file(s) to compile */
    @@Tool.option('')
    inputs: File[];

    /** Name of the output object file to generate. */
    @@Tool.option('-o')
    out: string;

    /** Reference file(s) for compilation */
    @@Tool.option('-r')
    references?: File[];

    /** Emit debugging information */
    @@Tool.option('-g')
    emitDebugInformation?: boolean;

    /** Assembly(ies) with a library initializer */
    @@Tool.option('--initassembly')
    initAssemblies?: string[];

    /** Generate complete metadata for types */
    @@Tool.option('--completetypemetadata')
    completeTypeMetadata?: boolean;

    /** Emit data to support generating stack trace strings at runtime */
    @@Tool.option('--stacktracedata')
    stackTraceData?: boolean;

    /** Consider all non-framework assemblies dynamically used */
    @@Tool.option('--rootallapplicationassemblies')
    rootAllApplicationAssemblies?: boolean;

    /** Scan IL for reflection patterns */
    @@Tool.option('--scanreflection')
    scanReflection?: boolean;

    // TODO: add other options... (and then update the 'compile' method)

    // BuildXL specific args

    /** Any additional dependencies */
    dependencies?: Transformer.InputArtifact[];
}

@@public
export interface Result {
    binary: DerivedFile
}

@@public
export function compile(args: Arguments): Result {
    args = Object.merge(defaultArgs, args);

    const outDir = Context.getNewOutputDirectory(args.out + "-ilc");
    const outObjPath = outDir.combine(args.out);

    let ilcArguments: Argument[] = [
        Cmd.startUsingResponseFile(true),

        Cmd.option("-o:",              Artifact.output(outObjPath)),
        Cmd.options("-r:",             Artifact.inputs(args.references)),
        Cmd.options("--initassembly:", args.initAssemblies),

        Cmd.flag("-g",                             args.emitDebugInformation),
        Cmd.flag("--stacktracedata",               args.stackTraceData),
        Cmd.flag("--scanreflection",               args.scanReflection),
        Cmd.flag("--completetypemetadata",         args.completeTypeMetadata),
        Cmd.flag("--rootallapplicationassemblies", args.rootAllApplicationAssemblies),

        Cmd.files(args.inputs),
    ];

    let ilcExecuteArgs : Transformer.ExecuteArguments = {
        tool: args.tool || ilcTool,
        arguments: ilcArguments,
        workingDirectory: outDir,
        tags: [...(args.tags || []), "compile-native" ],
        description: args.description,
        dependencies: (args.dependencies || []).filter(d => d !== undefined)
    };

    let executeResult = Transformer.execute(ilcExecuteArgs);

    return {
        binary: executeResult.getOutputFile(outObjPath)
    };
}
