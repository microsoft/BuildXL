/**
 * Defines three pips that produce (->) and consume (<-) the following artifacts:
 * 
 * pipA -> fileA
 * 
 * pipB <- fileA
 *      -> dirB
 * 
 * pipC <- dirB
 *      -> fileC1
 *      -> fileC2 
 * 
*/

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

export const cmdTool: Transformer.ToolDefinition = {
    exe: f`${Environment.getPathValue("COMSPEC")}`,
    dependsOnWindowsDirectories: true,
};

const wd = d`${Context.getMount("Out").path}`;
const fileA = f`${Context.getMount("Out").path}\fileA.out`;
const dirB = d`${Context.getMount("Out").path}\dirB`;
const fileC1 = f`${Context.getMount("Out").path}\fileC1.out`;
const fileC2 = f`${Context.getMount("Out").path}\fileC2.out`;

// The first pip writes "pipA" into its output file.
export const pipA = Transformer.execute({
    tool: cmdTool,
    arguments: [
        Cmd.argument("/D"),
        Cmd.argument("/C"),
        Cmd.argument("echo pipA > fileA.out"),
    ],
    outputs: [
        fileA
    ],
    workingDirectory: wd,
});

// The second pip creates an output directory and writes the content of pipA's output into a file under that directory.
const pipA_output = pipA.getOutputFile(fileA.path);
export const pipB = Transformer.execute({
    tool: cmdTool,
    arguments: [
        Cmd.argument("/D"),
        Cmd.argument("/C"),
        Cmd.argument("mkdir dirB & echo fileA content is > dirB/fileA_content.out & type fileA.out >> dirB/fileA_content.out"),
    ],
    dependencies: [
        pipA_output
    ],
    outputs: [
        // Note: the output of the pip is an exclusive opaque directory.
        dirB
    ],
    workingDirectory: wd,
});

// The third pip creates two output files with info about the dir pipB produced.
const pipB_output = pipB.getOutputDirectory(dirB);
export const pipC = Transformer.execute({
    tool: cmdTool,
    arguments: [
        Cmd.argument("/D"),
        Cmd.argument("/C"),
        Cmd.argument("echo dir dirB > fileC1.out & dir dirB >> fileC1.out & type dirB\\fileA_content.out > fileC2.out"),
    ],
    dependencies: [
        pipB_output
    ],
    outputs: [
        fileC1,
        fileC2
    ],
    workingDirectory: wd,
});
