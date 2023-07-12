import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const cmdTool: Transformer.ToolDefinition = {
    exe: f`${Environment.getPathValue("ComSpec")}`,
    dependsOnWindowsDirectories: true,
    untrackedDirectoryScopes: [
        d`${Environment.getPathValue("SystemRoot")}`
    ]
};

function getCmdToolDefinition(): Transformer.ToolDefinition {
    return {
        exe: f`${Environment.getPathValue("ComSpec")}`,
        dependsOnWindowsDirectories: true,
        untrackedDirectoryScopes: [
            d`${Environment.getPathValue("SystemRoot")}`
        ]
    };
}

function build() {
    Transformer.execute({
        tool: cmdTool,
        workingDirectory: d`.`,
        arguments: [
            Cmd.argument("/d"),
            Cmd.argument("/c"),
            Cmd.argument(Artifact.input(f`copy.cmd`)),
        ],
        outputs: [{ kind: "shared", directory: d`.` }],
        // Reference to the result of building Hello.csproj in the MSBuild resolver.
        //
        // The reference starts with importing the module, i.e., HelloWorldCS.
        // Each project is assigned witha symbol that resembles the relative path of
        // the project file from the root. The symbol represent an array of static
        // directories where the outputs of building the project using MSBuild are located.
        dependencies: importFrom("HelloWorldCS").Hello
    });
}

@@public
export const result = build();