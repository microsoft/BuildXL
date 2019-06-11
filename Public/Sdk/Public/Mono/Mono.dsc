// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as MacOS from "Sdk.MacOS";

const monoFrameworkPath = "MONO_HOME";
const monoExecutable = Context.getCurrentHost().os !== "win" ? findMonoExecutable() : undefined;

function findMonoExecutable() {
    let result = Environment.hasVariable(monoFrameworkPath)
        ? f`${Environment.getFileValue(monoFrameworkPath)}/mono`
        : f`/Library/Frameworks/Mono.framework/Versions/Current/Commands/mono`;

   if (!File.exists(result)) {
         Contract.fail(`Could not find Mono installed on your system at - please ensure Mono is installed per: https://www.mono-project.com/docs/getting-started/install/ and is accessable in your PATH!`);
   }

    return result;
}

const monoTool: Transformer.ToolDefinition = {
    exe: monoExecutable,
    dependsOnCurrentHostOSDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
    untrackedDirectoryScopes: [
        ...addIfLazy(Context.getCurrentHost().os !== "win", () => [
            d`${Context.getMount("DeploymentRoot").path.parent}`,
            d`${Context.getMount("UserProfile").path}/.mono`,
            d`/Library/Frameworks/Mono.framework`
        ]),
    ],
};

@@public
export function execute(args: Transformer.ExecuteArguments): Transformer.ExecuteResult {
    Contract.requires(args !== undefined);
    Contract.requires(args.tool !== undefined);

    const out = Context.getNewOutputDirectory("mono");
    const execArgs = <Transformer.ExecuteArguments>{
        tool: monoTool,
        tags: args.tags,
        arguments: [
            Cmd.argument(Artifact.input(args.tool.exe)),
            ...(args.arguments || [])
        ],
        allowUndeclaredSourceReads: args.allowUndeclaredSourceReads,
        workingDirectory: args.workingDirectory,
        dependencies: args.dependencies,
        outputs: args.outputs,
        consoleOutput: p`${out}/stdout.txt`,
        consoleError: p`${out}/stderr.txt`
    };

    return Transformer.execute(execArgs);
}