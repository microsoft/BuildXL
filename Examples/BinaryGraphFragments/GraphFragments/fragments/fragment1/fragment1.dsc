/**
 * pipA1 -> fileA
 */

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const cmdTool: Transformer.ToolDefinition = {
    exe: f`${Environment.getPathValue("COMSPEC")}`,
    dependsOnWindowsDirectories: true,
};

const wd = d`${Context.getMount("Out").path}`;
const fileA = f`${Context.getMount("Out").path}\fileA.out`;

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
