// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Transformer {

    /**
     * Copies the input to the provided output path setting the result to the given unix-style permissions
     * E.g. chmod("u+x", input, output)
     * On Windows, it just does a file copy without changing any file permissions
     */
    @@public
    export function chmod(permissions: string, input: File, output: Path): File {
        const currentHost = Context.getCurrentHost();

        if (currentHost.os === "macOS" || currentHost.os === "unix") {
            const result = Transformer.execute({
                tool: {
                    exe: f`/bin/bash`,
                    dependsOnCurrentHostOSDirectories: true
                },
                workingDirectory: d`${output.parent}`,
                arguments: [ 
                    Cmd.argument("-c"),
                    Cmd.rawArgument('"'),
                    Cmd.args([ "cp", Artifact.input(input), Artifact.output(output) ]),
                    Cmd.rawArgument(" && "),
                    Cmd.args([ "chmod", permissions, Artifact.none(output) ]),
                    Cmd.rawArgument('"')
                ]
            });

            return result.getOutputFile(output);
        }
        else {
            return Transformer.copyFile(input, output);
        }
    }

    /**
     * Calls chmod("u+x", input, output)
     */
    @@public
    export function makeExecutable(input: File, output: Path): File {
        return chmod("u+x", input, output);
    }
}