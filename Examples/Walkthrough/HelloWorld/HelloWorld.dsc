import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

const outputDir = d`${Context.getMount("ObjectRoot").path}`;

// Compile main.cpp to main.exe.
const mainExePath = p`${outputDir}/main.exe`;
const mainCompileResult = Transformer.execute({
    tool: {
        exe: Context.isWindowsOS() ? f`C:/msys64/ucrt64/bin/g++.exe` : f`/usr/bin/g++`,
        dependsOnCurrentHostOSDirectories: true,
        prepareTempDirectory: true,
        untrackedDirectoryScopes: Context.isWindowsOS()
            ? [d`C:/msys64/ucrt64`]
            : undefined
    },
    arguments: [
        Cmd.argument(Artifact.input(f`main.cpp`)),
        Cmd.option("-o", Artifact.output(mainExePath))
    ],
    workingDirectory: d`.`,
    environmentVariables: Context.isWindowsOS()
        ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
        : undefined
});

// Write input file for main.exe.
const mainInput = Transformer.writeAllLines(p`${outputDir}/main.in`, ["Hello, world!"]);

// Run main.exe to produce main.out.
const mainExe = mainCompileResult.getOutputFile(mainExePath);
const mainOutputPath = p`${outputDir}/main.out`;
const mainRunResult = Transformer.execute({
    tool: {
        exe: mainExe,
        dependsOnCurrentHostOSDirectories: true
    },
    arguments: [
        Cmd.argument(Artifact.input(mainInput)),
        Cmd.argument(Artifact.output(mainOutputPath))
    ],
    workingDirectory: outputDir,
    environmentVariables: Context.isWindowsOS()
        ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
        : undefined
});

// Copy main.out to main_copy.out.
const mainOutputCopy = Transformer.copyFile(mainRunResult.getOutputFile(mainOutputPath), p`${outputDir}/main_copy.out`);
