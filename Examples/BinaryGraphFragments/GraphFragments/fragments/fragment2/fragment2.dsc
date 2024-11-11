/**
 * pipB1 <- fileA
 *       -> dirB
  */

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const cmdTool: Transformer.ToolDefinition = {
    exe: f`${Environment.getPathValue("COMSPEC")}`,
    dependsOnWindowsDirectories: true,
};

const wd = d`${Context.getMount("Out").path}`;
const fileA = f`${Context.getMount("Out").path}\fileA.out`;
const dirB = d`${Context.getMount("Out").path}\dirB`;

// `fileA.out` is produced by pip (i.e., pipA in fragment1) that is not a part of this set of spec files.
// Still, we need to specify that fileA is an input of pipB. To do this, we call a special API and
// reference the file just by its path. Once it's done, we can use `pipA_output` variable like any other output.
//
// When a graph fragment is generated, there will be no errors about a producer of this file being unknown.
// The producer must be known when this graph fragment is added to a build, otherwise a build will result in an error.
const pipA_output = Unsafe.outputFile(fileA.path);

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
        dirB
    ],
    workingDirectory: wd,
});
