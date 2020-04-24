// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

export namespace Xcode {
    const userName = Environment.getStringValue("USER") || "";

    @@public
    export type Action =
        "build"
        | "build-for-testing"
        | "analyze"
        | "archive"
        | "test"
        | "test-without-building"
        | "install"
        | "install-src"
        | "clean";

    @@public
    export interface HeaderSearchPath {
        /** Type of header search path location */
        type: "system" | "user";

        /** Location where the custom headers can be found */
        directory: StaticDirectory;

        /** Recursively enumrate target location */
        recursive: boolean;
    }

    @@public
    export interface Arguments {
        /** Indicates if llbuild or the legacy build engine should be used */
        useModernBuildSystem: boolean;
        
        /** Location where the outputs go */
        derivedDataPath: Directory;

        /** Statically declared outputs */
        declaredOutputs?: Transformer.Output[];

        /** Override the default tool definition (see `tool`). */
        tool?: Transformer.ToolDefinition;

        /** Build this project. */
        project?: StaticDirectory;

        /** Build this target. */
        target?: string;

        /** Build all the targets in the specified project. */
        allTargets?: boolean;

        /** Build this workspace. */
        workspace?: Directory;

        /** Build this scheme.  Required if building a workspace. */
        scheme?: string;

        /** Use this build configuration when building each target. */
        configuration?: string;

        /** Use this architecture when building each target. */
        arch?: string;

        /** One or more actions to perform. If not specified, "build" is performed by default. */
        actions?: Action[];

        /** Static dependencies */
        dependencies?: Transformer.InputArtifact[];

        /** Exclusive semaphores to acquire */
        semaphores?: string[];

        /** override xcconfig */
        xcconfig?: File;

        /** override xcodebuild default location */
        overrideXcodeBuildPath?: File;

        /** allow for a custom header search locations */
        headerSearchPaths?: HeaderSearchPath[];
    }

    function modernBuildSystemIndicatorToString(useModernBuildSystem: boolean): string {
        return useModernBuildSystem ? "YES" : "NO";
    }

    @@public
    export function execute(args: Arguments): Transformer.ExecuteResult {
        Contract.requires(args.derivedDataPath !== undefined);

        const wd = Context.getNewOutputDirectory("xcodebuild");
        
        const customSystemHeaderSearchPaths = (args.headerSearchPaths || []).filter(hp => hp.type === "system").map(hp => p`${hp.directory.path}/${hp.recursive ? '**' : ''}`);
        const customUserHeaderSearchPaths = (args.headerSearchPaths || []).filter(hp => hp.type === "user").map(hp => p`${hp.directory.path}/${hp.recursive ? '**' : ''}`);

        const exeArgs: Transformer.ExecuteArguments = {
            tool: args.tool || {
                exe: args.overrideXcodeBuildPath || f`/usr/bin/xcodebuild`,
                dependsOnCurrentHostOSDirectories: true,
                untrackedFiles: [
                    f`/Users/${userName}/Library/Developer/Xcode/UserData/IDEEditorInteractivityHistory`
                ]
            },
            workingDirectory: wd,
            consoleOutput: p`${wd}/stdout.txt`,
            arguments: [
                Cmd.flag("-allTargets ", args.allTargets),
                Cmd.args(args.actions),
                Cmd.option("-project ", Artifact.input(args.project)),
                Cmd.option("-target ", args.target),
                Cmd.option("-workspace ", Artifact.none(args.workspace)),
                Cmd.option("-scheme ", args.scheme),
                Cmd.option("-configuration ", args.configuration),
                Cmd.option("-arch ", args.arch),
                Cmd.option("-derivedDataPath ", Artifact.output(args.derivedDataPath)),
                Cmd.option("-xcconfig ", Artifact.input(args.xcconfig)),
                
                Cmd.option("SYSTEM_HEADER_SEARCH_PATHS=", Cmd.join(" ", customSystemHeaderSearchPaths)),
                Cmd.option("HEADER_SEARCH_PATHS=", Cmd.join(" ", customUserHeaderSearchPaths)),
                Cmd.option("-UseModernBuildSystem=", modernBuildSystemIndicatorToString(args.useModernBuildSystem))
            ],
            acquireSemaphores: (args.semaphores || []).map(name => <Transformer.SemaphoreInfo>{
                name: name,
                limit: 1,
                incrementBy: 1
            }),
            outputs: args.declaredOutputs,
            dependencies: args.dependencies,
            allowedSurvivingChildProcessNames: [
                ...(args.useModernBuildSystem ? [ "XCBBuildService"] : [])
            ]
        };

        // Debug.writeLine([
        //     ``,
        //     `=========================================================`,
        //     ` *** cmd line:  ${Debug.dumpData(exeArgs.tool.exe)}${" "}${Debug.dumpArgs(exeArgs.arguments)}`,
        //     `=========================================================`
        // ].join("\n"));

        return Transformer.execute(exeArgs);
    }
}