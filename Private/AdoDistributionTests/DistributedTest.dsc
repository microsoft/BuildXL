// Defines two pips that when executed in order write "Hello World" to Out/Bin/file.txt
// We force the second pip to run on a remote worker

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const runningInWindows = Context.getCurrentHost().os === "win";

export const cmdTool: Transformer.ToolDefinition = 
    {
        exe: Context.isWindowsOS() ? f`${Environment.getPathValue("COMSPEC")}` : f`/bin/bash`,
        dependsOnCurrentHostOSDirectories: true,
    };

const wd = d`${Context.getMount("Out").path}`;
const outFile = f`${Context.getMount("Out").path}\file.out`;

// The helloPip runs the cmdTool (cmd.exe) to write "Hello" to file.out
// The pip is executed in the working directory which is specified by the pip as the "Out" mount. The "Out" mount was defined as "Out\Bin" in config.dsc
export const helloPip = Transformer.execute({
    tool: cmdTool,
    arguments: [
        ...addIf(runningInWindows, Cmd.argument("/D"), Cmd.argument("/C")),
        ...addIf(!runningInWindows, Cmd.argument("-c")),
        Cmd.argument("echo Hello > file.out"),
    ],
    outputs: [
        outFile
    ],
    workingDirectory: wd,
});

// The worldPip runs the cmdTool (cmd.exe) to append "World" to file.out
// For the correct output of "Hello World", the worldPip must execute after the helloPip
// To guarantee that BuildXL executed helloPip first, the worldPip specifies the output of helloPip ("helloPip.getOutputFile(outFile.path)") as a dependency
// BuildXL will ensure that all dependencies of worldPip are produced before executing worldPip
const appendFile = helloPip.getOutputFile(outFile.path);
export const worldPip = Transformer.execute({
    tool: cmdTool,
    tags: ["buildxl.internal:runRemotely"], // force this pip to run on a remote worker
    priority: 99, // indicate that this is used for test purposes so the tag above applies
    arguments: [
        ...addIf(runningInWindows, Cmd.argument("/D"), Cmd.argument("/C")),
        ...addIf(!runningInWindows, Cmd.argument("-c")),
        Cmd.argument("echo World >> file.out"),
    ],
    dependencies: [
        appendFile
    ],
    outputs: [
        appendFile
    ],
    workingDirectory: wd,
});
