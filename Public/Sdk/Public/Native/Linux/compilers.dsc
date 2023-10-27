// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

namespace Linux.Compilers {
    
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };

    const isLinux = Context.getCurrentHost().os === "unix";

    /**
     * GCC compiler
     */
    @@public
    export const gccTool = compilerTool("gcc");
    
    /**
     * G++ compiler
     */
    @@public
    export const gxxTool = compilerTool("g++");
    
    /**
     * Arguments for compiling a source file.
     */
    @@public
    export interface CompilerArguments {
        sourceFile: SourceFile,
        defines?: string[],
        headers?: File[],
        includeDirectories?: Directory[]
    }

    /**
     * Compiles a single source file. The compiler (g++ or gcc) is picked based off the extension
     * of the source file.
     */
    @@public
    export function compile(args: CompilerArguments): DerivedFile {
        const isDebug = qualifier.configuration === "debug";
        const isCpp = args.sourceFile.extension === a`.cpp`;
        const compiler = isCpp ? gxxTool : gccTool;
        const outDir = Context.getNewOutputDirectory(compiler.exe.name);
        const objFile = p`${outDir}/${args.sourceFile.name.changeExtension(".o")}`;
        
        const result = Transformer.execute({
            tool: compiler,
            workingDirectory: outDir,
            dependencies: args.headers || [],
            arguments: [
                Cmd.argument(Artifact.input(args.sourceFile)),
                Cmd.option("-o", Artifact.output(objFile)),
                Cmd.argument("-c"),
                Cmd.argument("-fPIC"),
                Cmd.options("-I", (args.includeDirectories || []).map(Artifact.none)),
                Cmd.options("-D", args.defines || []),
                Cmd.option("-D", isDebug ? "_DEBUG" : "_NDEBUG"),
                Cmd.option("-O", isDebug ? "g" : "3"),
                ...addIf(isDebug, Cmd.argument("-g")),
                ...addIf(isCpp, Cmd.argument("--std=c++17"))
            ]
        });

        return result.getOutputFile(objFile);
    }

    /**
     * Arguments for linking a collection of object files
     */
    @@public
    export interface LinkerArguments {
        tool: Transformer.ToolDefinition,
        outputName: PathAtom,
        objectFiles: DerivedFile[],
        libraries?: string[],
    }

    /**
     * Links a collection of object files
     */
    @@public
    export function link(args: LinkerArguments): DerivedFile {
        const isLib = args.outputName.extension === a`.so`;
        const outDir = Context.getNewOutputDirectory(args.tool.exe.name);
        const outFile = p`${outDir}/${args.outputName}`;
        const result = Transformer.execute({
            tool: args.tool,
            workingDirectory: outDir,
            arguments: [
                ...addIf(isLib, Cmd.argument("-shared")),
                Cmd.args(args.objectFiles.map(Artifact.input)),
                Cmd.option("-o", Artifact.output(outFile)),
                Cmd.options("-l", args.libraries || [])
            ]
        });

        return result.getOutputFile(outFile);
    }

    function compilerTool(compilerName: string) : Transformer.ToolDefinition {
        return {
            exe: f`/usr/bin/${compilerName}`,
            dependsOnCurrentHostOSDirectories: true,
            prepareTempDirectory: true,
            untrackedDirectoryScopes: [ d`/lib` ],
            untrackedFiles: [],
            runtimeDependencies: [f`/usr/lib64/ld-linux-x86-64.so.2`]
        };
    }
}