// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

function produceFramework(out: Directory) {
    const rootFrameworkDirPath = d`${out}/MyApp.framework`;

    /* 
        Creates the following layout:

        MyApp.framework/
        ├── Versions
        │   ├── A
        │   │   ├── file1
        │   │   └── sym-loop -> ../A
        │   ├── Current -> A
        │   └── SCurrent -> Current
        └── file1 -> Versions/Current/file1
   */
    const bashExecuteResult = Bash.runBashCommand(
        "mk-frm", 
        join(
            Cmd.rawArgument(";"), 
            [
                Cmd.args([ "mkdir", "-p", Artifact.sharedOpaqueOutput(rootFrameworkDirPath) ]),
                Cmd.args([ "cd", Artifact.none(rootFrameworkDirPath) ]),
                ...[
                    "mkdir -p Versions/A",
                    "printf 'Hello World' > Versions/A/file1",
                    "ln -sf ../A Versions/A/sym-loop",
                    "ln -sf A Versions/Current",
                    "ln -sf Current Versions/SCurrent",
                    "ln -sf Versions/Current/file1 file1",
                    "printf '!' >> file1",
                    "printf '!' >> Versions/Current/file1",
                    "printf '!' >> Versions/SCurrent/file1",
                    "printf '!' >> Versions/Current/sym-loop/file1",
                    "printf '!' >> Versions/Current/sym-loop/sym-loop/file1",
                ].map(Cmd.rawArgument),
            ]
        ),
        true);
    return bashExecuteResult.getOutputDirectory(rootFrameworkDirPath);
}

@@public
export const test1 = Bash.isMacOS && (() => {
    const outDir = Context.getNewOutputDirectory("test-frameworks");
    const frameworkRootDir = produceFramework(outDir);

    Bash.runBashCommand(
        "run-file-directly",
        join(
            Cmd.rawArgument(" && "),
            [
                Cmd.args([ "cd", Artifact.input(frameworkRootDir) ]),
                Cmd.args([ "cat", "Versions/A/file1" ])
            ]
        ),
        true);

    Bash.runBashCommand(
        "run-file-via-symlink",
        join(
            Cmd.rawArgument(" && "),
            [
                Cmd.args([ "cd", Artifact.input(frameworkRootDir) ]),
                Cmd.args([ "cat", "file1" ])
            ]
        ),
        true);

    Bash.runBashCommand(
        "run-file-via-current-symlink",
        join(
            Cmd.rawArgument(" && "),
            [
                Cmd.args([ "cd", Artifact.input(frameworkRootDir) ]),
                Cmd.args([ "cat", "Versions/SCurrent/file1" ])
            ]
        ),
        true);

    Bash.runBashCommand(
        "run-file-via-symloop",
        join(
            Cmd.rawArgument(" && "),
            [
                Cmd.args([ "cd", Artifact.input(frameworkRootDir) ]),
                Cmd.args([ "cat", "Versions/A/sym-loop/file1" ])
            ]
        ),
        true);
})();

function join<T>(sep: T, args: T[]): T[] {
    return args.reduce(
        (acc, elem) => acc.length === 0 ? [ elem ] : [ ...acc, sep, elem ],
        [] as T[]);
}
