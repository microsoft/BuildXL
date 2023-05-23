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

// Run main.exe, read files in Inputs directory, and produce output files in Staging directory.
const mainExe = mainCompileResult.getOutputFile(mainExePath);
const sealedInputDir = Transformer.sealSourceDirectory(d`Inputs`, Transformer.SealSourceDirectoryOption.allDirectories);
const stagingDirPath = Context.getNewOutputDirectory("Staging");
const mainRunStagingResult = Transformer.execute({
    tool: {
        exe: mainExe,
        dependsOnCurrentHostOSDirectories: true
    },
    arguments: [
        Cmd.argument(Artifact.input(sealedInputDir)),
        Cmd.argument(Artifact.output(stagingDirPath))
    ],
    workingDirectory: d`.`,
    environmentVariables: Context.isWindowsOS()
        ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
        : undefined
});

// Run main.exe, read files in Staging directory, and produce output files in Final directory.
const stagingDir = mainRunStagingResult.getOutputDirectory(stagingDirPath);
const finalDirPath = d`${outputDir}/Final`;
const mainRunFinalResult = Transformer.execute({
    tool: {
        exe: mainExe,
        dependsOnCurrentHostOSDirectories: true
    },
    arguments: [
        Cmd.argument(Artifact.input(stagingDir)),
        Cmd.argument(Artifact.output(finalDirPath))
    ],
    workingDirectory: d`.`,
    environmentVariables: Context.isWindowsOS()
        ? [{ name: "PATH", value: p`C:/msys64/ucrt64/bin` }]
        : undefined
});