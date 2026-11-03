// Defines two pips that when executed in order write "Hello World" to Out/Bin/file.txt
// We force the second pip to run on a remote worker

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

export const cmdTool: Transformer.ToolDefinition = 
    {
        exe: f`/bin/bash`,
        dependsOnCurrentHostOSDirectories: true,
    };

const wd = d`${Context.getMount("Out").path}`;
const outFile = f`${wd}/file.out`;
const hashFile = f`${wd}/hashInput.txt`;
const hashDirectory = d`${wd}/hashDirectory`;

// The helloPip runs Bash to write "Hello" and the hash inputs under the Out mount.
export const helloPip = Transformer.execute({
    tool: cmdTool,
    mustRunOnOrchestrator: true,
    arguments: [
        Cmd.argument("-c"),
        Cmd.argument("echo Hello > file.out && echo Hash input > hashInput.txt && mkdir -p hashDirectory && echo Directory member > hashDirectory/member.txt"),
    ],
    outputs: [
        outFile,
        hashFile,
        hashDirectory
    ],
    workingDirectory: wd,
});

// The worldPip runs remotely and validates file and opaque-directory hashes from the orchestrator's helloPip.
// Enumerating the working directory verifies the hash-only artifacts were not materialized without probing them.
// It then appends "World" to file.out, which remains a regular input/output dependency.
const appendFile = helloPip.getOutputFile(outFile.path);
export const worldPip = Transformer.execute({
    tool: cmdTool,
    tags: ["buildxl.internal:runRemotely"], // force this pip to run on a remote worker
    priority: 99, // indicate that this is used for test purposes so the tag above applies
    arguments: [
        Cmd.argument("-c"),
        // Hash-only artifacts are produced on the orchestrator; the worker needs their metadata, not their bytes.
        Cmd.argument(Cmd.concat([
            "fileHash='",
            Artifact.vsoHash(helloPip.getOutputFile(hashFile.path)),
            "'; directoryHash='",
            Artifact.vsoHash(helloPip.getOutputDirectory(hashDirectory)),
            '\'; [[ "$fileHash" == VSO0:* && "$directoryHash" == VSO0:* ]] && ',
            'for artifact in *; do [[ "$artifact" != hashInput.txt && "$artifact" != hashDirectory ]] || exit 1; done && echo World >> file.out',
        ])),
    ],
    dependencies: [
        appendFile
    ],
    outputs: [
        appendFile
    ],
    workingDirectory: wd,
});
