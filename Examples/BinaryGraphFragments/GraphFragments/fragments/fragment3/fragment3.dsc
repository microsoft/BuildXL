/**
 * pipC1 <- dirB
 *       -> fileC1
 *       -> fileC2 
  */

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const cmdTool: Transformer.ToolDefinition = {
    exe: f`${Environment.getPathValue("COMSPEC")}`,
    dependsOnWindowsDirectories: true,
};

const wd = d`${Context.getMount("Out").path}`;
const dirB = d`${Context.getMount("Out").path}\dirB`;
const fileC1 = f`${Context.getMount("Out").path}\fileC1.out`;
const fileC2 = f`${Context.getMount("Out").path}\fileC2.out`;

// Similar to the code in fragment2, here we also have a need to declare a dependency on an artifact that is produced
// outside of the bounds of the current fragment. So we use another special API to define a directory output.
const pipB_output = Unsafe.exOutputDirectory(dirB.path);

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
