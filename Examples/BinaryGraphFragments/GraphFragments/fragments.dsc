/**
 * Defines three pips that produces three binary graph fragments.
 * Each pip takes a set of build specs and calls BxlPipGraphFragmentGenerator to convert them into a build graph fragment.
 */

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const fragmentGeneratorTool: Transformer.ToolDefinition = {
    exe: f`${Environment.getPathValue("BUILDXL_BIN")}\BxlPipGraphFragmentGenerator.exe`,
    dependsOnWindowsDirectories: true,
};

const workingDirectory = d`${Context.getMount("Out").path}`;

const fragment1_sources = Transformer.sealSourceDirectory(d`${Context.getMount("fragments").path}\fragment1`);
export const fragment1 = generateGraphFragment(
    f`${fragment1_sources.path}\config.dsc`, 
    p`${Context.getMount("Out").path}\fragment1.out`, 
    fragment1_sources, 
    workingDirectory);

const fragment2_sources = Transformer.sealSourceDirectory(d`${Context.getMount("fragments").path}\fragment2`);
export const fragment2 = generateGraphFragment(
    f`${fragment2_sources.path}\config.dsc`, 
    p`${Context.getMount("Out").path}\fragment2.out`, 
    fragment2_sources, 
    workingDirectory);

const fragment3_sources = Transformer.sealSourceDirectory(d`${Context.getMount("fragments").path}\fragment3`);
export const fragment3 = generateGraphFragment(
    f`${fragment3_sources.path}\config.dsc`, 
    p`${Context.getMount("Out").path}\fragment3.out`, 
    fragment3_sources, 
    workingDirectory);

function generateGraphFragment(fragmentConfigFile: File, outputFilePath: Path, specsSourceDirectory: SourceDirectory, workingDirectory: Directory): Transformer.ExecuteResult {
    return Transformer.execute({
        tool: fragmentGeneratorTool,
        arguments: [
            // While this example uses only two arguments, the tool supports several other arguments (refer to "BxlPipGraphFragmentGenerator.exe /help" for more details).
            // The entry point into a build spec that we want to convert into a graph fragment.
            Cmd.option("/c:", Artifact.input(fragmentConfigFile)),
            // The output location of the created fragment.
            Cmd.option("/outputFile:", Artifact.output(outputFilePath))
        ],
        dependencies: [
            specsSourceDirectory
        ],
        workingDirectory: workingDirectory,
        // The input builds specs reference these env variables, so we need to pass them to the tool (alternatively, this can be done via /p: argument).
        environmentVariables: [
            {
                name: "BUILDXL_BIN",
                value: Environment.getPathValue("BUILDXL_BIN")
            },
            {
                name: "OUTPUT_DIR",
                value: Environment.getPathValue("OUTPUT_DIR")
            },
        ]
    });
}
