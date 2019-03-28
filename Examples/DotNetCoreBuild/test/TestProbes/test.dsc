// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

const numRepeats = 3;

@@public
export const testFileRead = Bash.runInBashSubshells(
    "testFileRead",
    numRepeats,
    [
        Artifact.input(f`/bin/cat`),
        Artifact.input(f`src-dir/src-file.txt`)
    ]);

@@public
export const testFileGetAttrs = Bash.runInBashSubshells(
    "testFileGetAttrs",
    numRepeats,
    [
        Artifact.input(f`/bin/ls`),
        "-l",
        Artifact.input(f`src-dir/src-file.txt`)
    ]);

@@public
export const testListDirectory = Bash.runInBashSubshells(
    "testListDirectory",
    numRepeats,
    [
        Artifact.input(f`/bin/ls`),
        Artifact.input(Transformer.sealSourceDirectory(d`src-dir`, Transformer.SealSourceDirectoryOption.topDirectoryOnly))
    ]);

@@public
export const testListLongDirectory = Bash.runInBashSubshells(
    "testListLongDirectory",
    numRepeats,
    [
        Artifact.input(f`/bin/ls`),
        "-l",
        Artifact.input(Transformer.sealSourceDirectory(d`src-dir`, Transformer.SealSourceDirectoryOption.topDirectoryOnly))
    ]);

@@public
export const testPresentFileProbe = Bash.runInBashSubshells(
    "testPresentFileProbe",
    numRepeats,
    [
        Artifact.input(f`/bin/test`),
        "-f",
        Artifact.input(f`src-dir/src-file.txt`)
    ]);

@@public
export const testAbsentFileProbe = Bash.runInBashSubshells(
    "testAbsentFileProbe",
    numRepeats,
    [
        Artifact.input(f`/bin/test`),
        "!",
        "-f",
        Artifact.none(p`src-dir/absent-file.txt`)
    ]);
