// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

@@public
export const readSymlinks = Bash.isMacOS && (() => {
    const outDir = Context.getNewOutputDirectory("ln");
    const outDiffSymlinkPath = p`${outDir}/symlink-to-usr-bin-diff`;

    // Execute
    //
    //   ln -s /usr/bin/diff <out-dir>/symlink-to-usr-bin-diff
    const symlinkToDiff = Bash.runBashCommand("symlink-to-diff", [
        Cmd.args([
            Artifact.input(f`/bin/ln`),
            "-sf",
            Artifact.input(f`/usr/bin/diff`),
            Artifact.output(outDiffSymlinkPath),
        ])
    ]).getOutputFile(outDiffSymlinkPath);

    // NOTE:
    //   f`symlink-to-src-file.txt` must be a symlink pointing to "src-file.txt"
    //   that exists before the build is started (concretely, it is created in
    //   a pre-build step in validate-build-kext.sh)
    //
    // Execute:
    //
    //   symlink-to-usr-bin-diff src-file.txt symlink-to-src-file.txt
    //
    // which should effectively be
    //
    //   /usr/bin/diff src-file.txt src-file.txt
    //
    // which should always exit with code 0
    Bash.runBashCommand("diff-symlink", [
        Cmd.args([
            Artifact.input(symlinkToDiff),
            Artifact.input(f`src-file.txt`),
            Artifact.input(f`symlink-to-src-file.txt`),
        ])
    ]);
})();
