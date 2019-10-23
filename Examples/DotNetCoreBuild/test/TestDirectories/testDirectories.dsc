// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Bash from "Bash";
import {Cmd, Artifact, Transformer} from "Sdk.Transformers";

// =============================================================================
// Exclusive opaque directory examples
// =============================================================================

@@public
export const writeArbitraryFileIntoExclusiveOpaqueDirectoryIsAllowed = Bash.isMacOS && (() => {
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
export const explicitOutputsByDifferentPipsAreAllowedInSharedOpaqueDirectory = Bash.isMacOS && (() => {
    const sodPath = Context.getNewOutputDirectory("sod");
    const sod = Artifact.sharedOpaqueOutput(sodPath);
    writeFileToDir(p`${sodPath}/explicit1.txt`, sod);
    writeFileToDir(p`${sodPath}/explicit2.txt`, sod);
})();

@@public
export const twoPipsWritingArbitraryFilesIntoSharedOpaqueDirectoryIsAllowed = Bash.isMacOS && (() => {
    const sodPath = Context.getNewOutputDirectory("sod-mix");
    const sod = Artifact.sharedOpaqueOutput(sodPath);
    writeFileToDir("arbitrary1", sod);
    writeFileToDir("arbitrary2", sod);
    writeFileToDir(p`${sodPath}/explicit3`, sod);
    const wrFile = Transformer.writeFile(p`${sodPath}/write-file.txt`, "Hi");
    Transformer.copyFile(wrFile, p`${sodPath}/copy-file.txt`);
})();

@@public
export const directoryDoubleWriteIsAllowedUnderASharedOpaque = Bash.isMacOS && (() => {
    const sodPath = Context.getNewOutputDirectory("sod");
    const sod = Artifact.sharedOpaqueOutput(sodPath);
    createDirectory(sod);
    createDirectory(sod);
})();

@@public
export const writeHardLinkInSharedOpaqueDirectoryIsAllowed = Bash.isMacOS && (() => {
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

@@public
export const moveDirectoryInsideSOD = Bash.isMacOS && (() => {
    const sodPath = Context.getNewOutputDirectory("sod-mov");
    const sod = Artifact.sharedOpaqueOutput(sodPath);
    const nestedDirTmpName = "nested-dir-tmp";
    const nestedDirFinalName = "nested-dir";
    Bash.runBashCommand("move-dir", [
        Cmd.args([ "cd", sod ]),
        Cmd.rawArgument(" && "),
        Cmd.args([ Artifact.input(f`/bin/mkdir`), nestedDirTmpName ]),
        Cmd.rawArgument(" && "),
        Cmd.args([ Artifact.input(f`/usr/bin/touch`), `${nestedDirTmpName}/file-before.txt` ]),
        Cmd.rawArgument(" && "),
        Cmd.args([ Artifact.input(f`/bin/mv`), nestedDirTmpName, nestedDirFinalName ]),
        Cmd.rawArgument(" && "),
        Cmd.args([ Artifact.input(f`/usr/bin/touch`), `${nestedDirFinalName}/file-after.txt` ]),
    ], true);
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
// Detecting obscure writes into a shared opaque directory
// =============================================================================

@@public
export const testSourceCshWithRedirect = Bash.isMacOS && (() => {
    const sod = Context.getNewOutputDirectory("sod-csh");

    const sodOutput = p`${sod}/csh-out.txt`;

    // ${sod}/cshToSource.csh :
    //   ls > ${sod}/csh-out.txt
    // (NOTE: using 'echo' instead of 'ls' would not result in MAC_VNODE_WRITE against the 'csh-out.txt' file)
    const cshToSource = Transformer.writeData(
        p`${sod}/cshToSource.csh`,
        {
            contents: [ "/bin/ls", ">", sodOutput ],
            separator: " "
        });

    // ${sod}/execute.sh :
    //   source ${sod}/cshToSource.csh
    const scriptToExecute = Transformer.writeData(
        p`${sod}\execute.sh`,
        {
            contents: [ "source", cshToSource.path ],
            separator: " "
        });

    // execute:
    //   /bin/csh ${sod}/execute.sh
    Transformer.execute({
        tool: {
            exe: f`/bin/csh`,
            dependsOnCurrentHostOSDirectories: true,
            prepareTempDirectory: true
        },
        arguments: [
            Cmd.argument(Artifact.none(scriptToExecute))
        ],
        workingDirectory: sod,
        dependencies: [
            cshToSource,
            scriptToExecute
        ],
        outputs: [
            { kind: "shared", directory: sod }
        ],
        implicitOutputs: [
            sodOutput // this ensures the build fails if no write file accesses were received for this file
        ]
    });
})();

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
        ? `${outFile}.txt`
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
    ], false);
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
    if (!Bash.isMacOS) return undefined;

    // Execute:
    //   cd <dir> && /bin/cat <fileName>
    return Bash.runBashCommand(hint, [
        Cmd.args(["cd", Artifact.input(dir)]),
        Cmd.rawArgument(" && "),
        Cmd.args([Artifact.input(f`/bin/cat`), fileName])
    ]);
}
