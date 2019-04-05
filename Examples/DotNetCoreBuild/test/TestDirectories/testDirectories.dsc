// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

// =============================================================================
// Exclusive opaque directory examples
// =============================================================================

@@public
export const writeArbitraryFileIntoExclusiveOpaqueDirectoryIsAllowed = !Context.isWindowsOS() && (() => {
    const outDir = Context.getNewOutputDirectory("od");
    const od1Path = d`${outDir}/od1`;
    const od2Path = d`${outDir}/od2`;
    const od1 = writeFileToDir("arbitrary1", Artifact.output(od1Path));
    const od2 = writeFileToDir("arbitrary2", Artifact.output(od2Path));
    listDirectories([od1, od2]);
})();

// =============================================================================
// Shared opaque directory examples
// =============================================================================

@@public
export const explicitOutputsByDifferentPipsAreAllowedInSharedOpaqueDirectory = !Context.isWindowsOS() && (() => {
    const sodPath = Context.getNewOutputDirectory("sod");
    const sod = Artifact.sharedOpaqueOutput(sodPath);
    writeFileToDir(p`${sodPath}/explicit1.txt`, sod);
    writeFileToDir(p`${sodPath}/explicit2.txt`, sod);
})();

@@public
export const twoPipsWritingArbitraryFilesIntoSharedOpaqueDirectoryIsAllowed = !Context.isWindowsOS() && (() => {
    const sodPath = Context.getNewOutputDirectory("sod-mix");
    const sod = Artifact.sharedOpaqueOutput(sodPath);
    writeFileToDir("arbitrary1", sod);
    writeFileToDir("arbitrary2", sod);
    writeFileToDir(p`${sodPath}/explicit3`, sod);
    const wrFile = Transformer.writeFile(p`${sodPath}/write-file.txt`, "Hi");
    Transformer.copyFile(wrFile, p`${sodPath}/copy-file.txt`);
})();

@@public
export const directoryDoubleWriteIsAllowedUnderASharedOpaque = !Context.isWindowsOS() && (() => {
    const sodPath = Context.getNewOutputDirectory("sod");
    const sod = Artifact.sharedOpaqueOutput(sodPath);
    createDirectory(sod);
    createDirectory(sod);
})();

@@public
export const writeHardLinkInSharedOpaqueDirectoryIsAllowed = !Context.isWindowsOS() && (() => {
    const sodPath = Context.getNewOutputDirectory("sod-with-links");
    const sod = Artifact.sharedOpaqueOutput(sodPath);

    // hardlink a source file
    linkFileIntoDirectory(f`testDirectories.dsc`, sod);

    // hardlink an output file
    const outFile = Transformer.writeAllText({
        outputPath: p`${Context.getNewOutputDirectory("write-file")}/writefile.txt`, 
        text: "hi"
    });
    linkFileIntoDirectory(outFile, sod);

    // symlink a source file
    linkFileIntoDirectory(f`module.config.dsc`, sod, true);
})();

// =============================================================================
// Seal directory examples
// =============================================================================

@@public
export const testSealedSourceDir = readFileFromDirectory(
    "read-seal-source-dir",
    Transformer.sealSourceDirectory(d`src-dir`, Transformer.SealSourceDirectoryOption.allDirectories),
    "src-file1.txt");

@@public
export const testSealedDir = readFileFromDirectory(
    "read-seal-dir",
    Transformer.sealDirectory({
        root: d`src-dir`, 
        files: globR(d`src-dir`, "*")
    }),
    "src-file1.txt");

@@public
export const testSealedPartialDir = readFileFromDirectory(
    "read-seal-partial-dir",
    Transformer.sealPartialDirectory(d`src-dir`, [f`src-dir/src-file1.txt`, f`src-dir/unused-file.txt`]),
    "src-file1.txt");

// =============================================================================
// Helper functions
// =============================================================================

function linkFileIntoDirectory(srcFile: File, outDir: Artifact, symbolic?: boolean): OpaqueDirectory {
    // Execute:
    //   ln -[s]f <srcFile> <outDir>
    const result = Bash.runBashCommand("link-to-dir", [
        Cmd.args([
            Artifact.input(f`/bin/ln`),
            symbolic ? "-sf" : "-f",
            Artifact.input(srcFile),
            outDir
        ])
    ], true);
    return result.getOutputDirectory(outDir.path as Directory);
}

/**
 * Schedules a pip that writes to a file (determined by 'outFile') into a directory
 * (identified by 'outDir').
 * 
 * If 'outFile' is a Path, that Path is used as an explicit output artifact;
 * if 'outfile' is a string, that string is used as a prefix to an arbitrary file name.
 * 
 * 'oudDir' should be some kind of output directory artifact, e.g, obtained either
 * by calling 'Artifact.output(...)' or 'Artifact.sharedOpaqueOutput(...)'.
 */
function writeFileToDir(outFile: string | Path, outDir: Artifact, hint?: string): OpaqueDirectory {
    const fileArgValue = typeof(outFile) === "string"
        ? `${outFile}-$(date +%Y-%m-%d_%H-%M).txt`
        : Artifact.output(outFile as Path);

    // Execute:
    //   cd <outDir> && date | tee <fileName>
    const result = Bash.runBashCommand(hint || "write-to-dir", [
        Cmd.args(["cd", outDir]),
        Cmd.rawArgument(" && "),
        Cmd.argument(Artifact.input(f`/bin/date`)),
        Cmd.rawArgument(" | "),
        Cmd.args([Artifact.input(f`/usr/bin/tee`), fileArgValue])
    ]);
    return result.getOutputDirectory(outDir.path as Directory);
}

function createDirectory(outDir: Artifact) {
    return Bash.runBashCommand("create-dir", [
        Cmd.argument(Artifact.input(f`/bin/mkdir`)),
        Cmd.argument("-p"),
        Cmd.argument(outDir)
    ], true);
}

function listDirectories(dirs: OpaqueDirectory[]): DerivedFile {
    // Execute:
    //   /bin/ls <dir1> <dir2> ...
    const r = Bash.runBashCommand("list-dirs", [
        Cmd.argument(Artifact.input(f`/bin/ls`)),
        Cmd.args(dirs.map(Artifact.input))
    ]);
    return r.getOutputFiles()[0];
}

function readFileFromDirectory(hint: string, dir: SourceDirectory | StaticDirectory, fileName: string): Transformer.ExecuteResult {
    if (Context.isWindowsOS()) return undefined;

    // Execute:
    //   cd <dir> && /bin/cat <fileName>
    return Bash.runBashCommand(hint, [
        Cmd.args(["cd", Artifact.input(dir)]),
        Cmd.rawArgument(" && "),
        Cmd.args([Artifact.input(f`/bin/cat`), fileName])
    ]);
}
