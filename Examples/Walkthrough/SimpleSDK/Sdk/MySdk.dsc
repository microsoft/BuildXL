import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

@@public
export interface CompileArgs
{
    cFile: File;
    includes?: File[];
    includeSearchDirs?: StaticDirectory[];
    optimize?: boolean;
}

@@public
export interface LinkArgs
{
    objFiles: File[];
    output: Path;
}

const compilerTool = {
    exe: Context.isWindowsOS() ? f`C:/msys64/ucrt64/bin/g++.exe` : f`/usr/bin/g++`,
    dependsOnCurrentHostOSDirectories: true,
    prepareTempDirectory: true,
    untrackedDirectoryScopes: Context.isWindowsOS()
        ? [d`C:/msys64/ucrt64`]
        : undefined
};

@@public
export function compile(args: CompileArgs) : File
{
    const outDir = Context.getNewOutputDirectory("compile");
    const objFile = p`${outDir}/${args.cFile.name.changeExtension(".o")}`;
    const result = Transformer.execute({
        tool: compilerTool,
        arguments: [
            Cmd.argument("-c"),
            Cmd.flag("-O3", args.optimize),
            Cmd.argument(Artifact.input(args.cFile)),
            Cmd.options("-I ", Artifact.inputs(args.includeSearchDirs)),
            Cmd.option("-o ", Artifact.output(objFile)),
        ],
        dependencies: args.includes || [],
        workingDirectory: d`${args.cFile.parent}`,
        environmentVariables: Context.isWindowsOS()
            ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
            : undefined
    });
    return result.getOutputFile(objFile);
}

@@public
export function link(args: LinkArgs) : File
{
    const result = Transformer.execute({
        tool: compilerTool,
        arguments: [
            Cmd.files(args.objFiles),
            Cmd.option("-o ", Artifact.output(args.output)),
        ],
        workingDirectory: d`${args.output.parent}`,
        environmentVariables: Context.isWindowsOS()
            ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
            : undefined
    });
    return result.getOutputFile(args.output);
}