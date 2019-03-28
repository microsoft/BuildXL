// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const pkgContents = importFrom("Microsoft.TypeScript.Compiler").Contents.all;

@@public
export const tool : Transformer.ToolDefinition = {
    exe: pkgContents.getFile(r`tools/tsc.exe`),
    runtimeDirectoryDependencies: [
        pkgContents
    ],
    dependsOnWindowsDirectories: true
};

/**
 * Schedules the execution of the TypeScript compiler
 */
@@public
export function compile(args: Arguments): Result {
    let workingDir = Context.getNewOutputDirectory("tsc");

    let baseOutputFilename = workingDir.combine(args.source.path.name);

    let outputFileName = baseOutputFilename.changeExtension(".js");
    let declarationFileName = baseOutputFilename.changeExtension( ".d.ts");
    let sourceMapFileName = baseOutputFilename.changeExtension(".js.map");

    let tscArguments : Argument[] =  [
        Cmd.argument(Artifact.input(args.source)),
        Cmd.option("-out ", Artifact.output(outputFileName)),
        Cmd.flag("--declaration", args.generateDeclaration),
        Cmd.flag("--sourcemap", args.generateSourceMap),
        Cmd.flag("--noImplicitAny", args.noImplicitAny),
        Cmd.flag("--removeComments", args.removeComments),
        // This generates, for example, "--target "ES3. Seems to happen only when output:false
        Cmd.option("--target ", args.targetVersion ? toTargetVersionString(args.targetVersion) : undefined)
    ];

    // TODO: need to add declarationFileName and sourceMapFileName as implicit outputs, but there is no way to do that now
    // Specifying these options now will fail

    // we shouldn't need to do this here, but at this point this check is not happening in 'Transformer.execute'
    let implicitInputDependencies = (args.tripleSlashReferences && args.tripleSlashReferences.filter(dep => dep !== undefined)) || [];

    let result = Transformer.execute({
        tool: args.tool || tool,
        arguments: tscArguments,
        workingDirectory: workingDir,
        dependencies: implicitInputDependencies,
    });

    return {
        jsFile: result.getOutputFile(outputFileName),
        declaration: args.generateDeclaration && result.getOutputFile(declarationFileName),
        sourceMap: args.generateSourceMap && result.getOutputFile(sourceMapFileName)
    };
}

/**
 *  Input arguments for the TypeScript compiler
 */
@@public
export interface Arguments extends Transformer.RunnerArguments {
    // Input source file
    source: File;

    // Generates corresponding .d.ts file if set to true
    generateDeclaration?: boolean;

    // Generates corresponding .map file if set to true
    generateSourceMap?: boolean;

    // Warn on expressions and declarations with an implied 'any' type
    noImplicitAny?: boolean;

    // Do not emit comments to output
    removeComments?: boolean;

    // Specify ECMAScript target version
    targetVersion?: Target;

    // Specifies a list of extra dependencies. This corresponds to the /// reference  path='file' references
    tripleSlashReferences?: File[];
}

/**
 *  ECMAScript target version.
 */
@@public
export const enum Target { eS3, eS5 }


/**
 *  Output of the TypeScript compiler
 */
@@public
export interface Result {
    // Generated javascript file
    jsFile: File;

    // Generated .d.ts declaration file
    declaration?: File;

    // Generated .js.map sourcemap file
    sourceMap?: File;
}

function toTargetVersionString(target: Target){
    return target.toString().toUpperCase();
}
