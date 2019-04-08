// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";
import {Transformer} from "Sdk.Transformers";

namespace Sdk.Tests {
    const sealedDirectory1 = Transformer.sealDirectory({
        root: d`src/seal1`, 
        files: [f`src/seal1/file1`]
    });
    const sealedDirectory2 = Transformer.sealDirectory({
        root: d`src/seal2`, 
        files: [f`src/seal2/file2`]
    });
    
    const mostFields: Transformer.ToolDefinition = {
        exe: f`src/tool/tool.exe`,
        description: "toolDescription",
        nestedTools: [{exe: f`src/tool/nested.exe`}],
        runtimeDependencies: [f`src/tool/helper.dll`],
        runtimeDirectoryDependencies: [sealedDirectory2],
        prepareTempDirectory: true,
        dependsOnWindowsDirectories: true,
        dependsOnAppDataDirectory: true,
        untrackedFiles: [f`src/tool/untrackedFile.txt`],
        untrackedDirectories: [d`src/tool/untrackedDirectory`],
        untrackedDirectoryScopes: [d`src/tool/untrackedDirectoryScope`],
        timeoutInMilliseconds: 123456,
        warningTimeoutInMilliseconds: 12345,
    };
    
    const smallTool: Transformer.ToolDefinition = {exe: f`src/tool/tool.exe`};
    
    @@Testing.unitTest()
    export function fullExecute() {
        const result = Transformer.execute({
            tool: mostFields,
            tags: ["aTag", "zTag"],
            description: "testDescription",
            arguments: [],
            workingDirectory: d`out/working`,
            dependencies: [f`src/input/file.txt`, sealedDirectory1],
            implicitOutputs: [
                p`out/outputFile1.txt`,
                p`out/outputFile2.txt`,
                p`out/outputFile3.txt`,
                d`out/dynamicSealedDirectory`,
            ],
            optionalImplicitOutputs: [
                p`out/optionalImplicitOutput1.txt`,
                p`out/optionalImplicitOutput2.txt`,
                p`out/optionalImplicitOutput3.txt`,
                p`out/optionalImplicitOutput4.txt`,
            ],
            consoleInput: f`src/stdIn.txt`,
            consoleOutput: p`out/stdOut.txt`,
            consoleError: p`out/stdErr.txt`,
            environmentVariables: [{name: "env1", value: "envValue1"}],
            warningRegex: "warningRegEx",
            acquireSemaphores: [
                {name: "semaphore1", limit: 2, incrementBy: 2},
                {name: "semaphore2", limit: 1, incrementBy: 1},
            ],
            acquireMutexes: ["mutex1", "mutex2"],
            successExitCodes: [
                0,
                1,
                2,
                4,
            ],
            tempDirectory: d`out/temp`,
            additionalTempDirectories: [d`out/extraTemp1`, d`out/extraTemp2`],
            unsafe: {
                untrackedPaths: [d`src/untrackedPathOnPip`],
                untrackedScopes: [d`src/untrackedScopeOnPip`],
                hasUntrackedChildProcesses: true,
                allowPreservedOutputs: true,
                passThroughEnvironmentVariables: ["env1"],
            },
            keepOutputsWritable: true,
        });
        
        Assert.areEqual(
            9,
            result.getOutputFiles().length
        );
        Assert.areEqual(
            p`out/outputFile1.txt`,
            result.getOutputFile(p`out/outputFile1.txt`).path
        );
        Assert.areEqual(
            p`out/dynamicSealedDirectory`,
            result.getOutputDirectory(d`out/dynamicSealedDirectory`).root.path
        );
    }

    @@Testing.unitTest()
    export function undefineValuesAreValidForPathLikeArrayLiterals() {
        const result = Transformer.execute({
            tool: mostFields,
            // Undefined are not allowed for tags
            tags: ["aTag", "zTag"],
            description: "testDescription",
            arguments: [undefined],
            workingDirectory: d`out/working`,
            dependencies: [f`src/input/file.txt`, sealedDirectory1, undefined],
            implicitOutputs: [
                p`out/outputFile1.txt`,
                p`out/outputFile2.txt`,
                p`out/outputFile3.txt`,
                d`out/dynamicSealedDirectory`,
                undefined,
            ],
            optionalImplicitOutputs: [
                p`out/optionalImplicitOutput1.txt`,
                p`out/optionalImplicitOutput2.txt`,
                p`out/optionalImplicitOutput3.txt`,
                p`out/optionalImplicitOutput4.txt`,
                undefined,
            ],
            consoleInput: f`src/stdIn.txt`,
            consoleOutput: p`out/stdOut.txt`,
            consoleError: p`out/stdErr.txt`,
            // undefined is not allowed here
            environmentVariables: [{name: "env1", value: "envValue1"}],
            warningRegex: "warningRegEx",
            // undefined is not allowed here
            acquireSemaphores: [
                {name: "semaphore1", limit: 2, incrementBy: 2},
                {name: "semaphore2", limit: 1, incrementBy: 1},
            ],
            // undefined is not allowed here
            acquireMutexes: ["mutex1", "mutex2"],
            // undefined is not allowed here
            successExitCodes: [
                0,
                1,
                2,
                4,
            ],
            tempDirectory: d`out/temp`,
            additionalTempDirectories: [d`out/extraTemp1`, d`out/extraTemp2`, undefined],
            unsafe: {
                untrackedPaths: [d`src/untrackedPathOnPip`, undefined],
                untrackedScopes: [d`src/untrackedScopeOnPip`, undefined],
                hasUntrackedChildProcesses: true,
                allowPreservedOutputs: true,
                // undefined is not allowed here.
                passThroughEnvironmentVariables: ["env1"],
            },
            keepOutputsWritable: true,
        });
        
        Assert.areEqual(
            9,
            result.getOutputFiles().length
        );
        Assert.areEqual(
            p`out/outputFile1.txt`,
            result.getOutputFile(p`out/outputFile1.txt`).path
        );
        Assert.areEqual(
            p`out/dynamicSealedDirectory`,
            result.getOutputDirectory(d`out/dynamicSealedDirectory`).root.path
        );
    }
    
    @@Testing.unitTest()
    export function mustSpecifyOneOutput() {
        Testing.expectFailure(
            () => Transformer.execute({tool: {exe: f`src/tool.exe`}, workingDirectory: d`out/working`, arguments: []}),
            {
                code: 218,
                content: "At least one output file or opaque directory is required.",
            }
        );
    }
    
    @@Testing.unitTest()
    export function minimalOutputfile() {
        const result = Transformer.execute({
            tool: {exe: f`src/tool.exe`},
            workingDirectory: d`out/working`,
            arguments: [],
            implicitOutputs: [p`out/file.out`],
        });
        
        Assert.areEqual(
            1,
            result.getOutputFiles().length
        );
        Assert.areEqual(
            p`out/file.out`,
            result.getOutputFile(p`out/file.out`).path
        );
    }
    
    @@Testing.unitTest()
    export function minimalOutputDirectory() {
        const result = Transformer.execute({
            tool: {exe: f`src/tool.exe`},
            workingDirectory: d`out/working`,
            arguments: [],
            implicitOutputs: [d`out/outputFolder`],
        });
        
        Assert.areEqual(
            0,
            result.getOutputFiles().length
        );
        Assert.areEqual(
            p`out/outputFolder`,
            result.getOutputDirectory(d`out/outputFolder`).root.path
        );
    }
    
    @@Testing.unitTest()
    export function rewrittenFiles() {
        const copyFile = Transformer.copyFile(f`src/input`, p`out/destination`);
        const result = Transformer.execute({
            tool: {exe: f`src/stringInput.exe`},
            workingDirectory: d`out/working`,
            arguments: [],
            dependencies: [copyFile],
            implicitOutputs: [copyFile],
        });
        
        Assert.areEqual(
            1,
            result.getOutputFiles().length
        );
        Assert.areEqual(
            copyFile.path,
            result.getOutputFile(copyFile.path).path
        );
        Assert.notEqual(
            copyFile,
            result.getOutputFile(copyFile.path)
        );
    }
    
    @@Testing.unitTest()
    export function stdInData() {
        Transformer.execute({
            tool: {exe: f`src/stringInput.exe`},
            workingDirectory: d`out/working`,
            arguments: [],
            implicitOutputs: [p`out/file1.out`],
            consoleInput: "StringInputForConsole",
        });
        
        Transformer.execute({
            tool: {exe: f`src/dataInput.exe`},
            workingDirectory: d`out/working`,
            arguments: [],
            implicitOutputs: [p`out/file2.out`],
            consoleInput: {
                separator: "--",
                contents: [
                    "str",
                    1,
                    a`atom`,
                    p`path`,
                    r`relative/path`,
                    {contents: ["nested"]},
                ],
            },
        });
    }
    
    @@Testing.unitTest()
    export function environmentVariables() {
        Transformer.execute({
            tool: {exe: f`src/environmentVariables.exe`},
            workingDirectory: d`out/working`,
            arguments: [],
            environmentVariables: [
                {name: "string", value: "stringValue"},
                {
                    name: "bool",
                    value: true,
                },
                {name: "number", value: 1},
                {name: "path", value: p`path`},
                {name: "paths", value: [p`path1`, p`path2`]},
                {name: "file", value: f`file`},
                {name: "files", value: [f`file1`, f`file2`]},
                {name: "dir", value: d`dir`},
                {name: "dirs", value: [d`dir1`, d`dir1`]},
                {name: "staticDir", value: sealedDirectory1},
                {name: "staticDirs", value: [sealedDirectory1, sealedDirectory2]},
                {name: "pathsWithSepartor", value: [p`path1`, p`path2`], separator: "----"},
            ],
            implicitOutputs: [p`out/file.out`],
        });
    }
    @@Testing.unitTest()
    export function runtimeEnvironment() {
        const tool = {
            exe: f`src/stringInput.exe`,
            runtimeEnvironment: {
                minimumOSVersion: {
                    major: 1,
                    minor: 2,
                    revision: 3,
                    buildNumber: 4,
                },
                maximumOSVersion: {
                    major: 11,
                    minor: 12,
                    revision: 13,
                    buildNumber: 14,
                },
                minimumClrVersion: {
                    major: 21,
                    minor: 22,
                    revision: 23,
                    buildNumber: 24,
                },
                maximumClrVersion: {
                    major: 31,
                    minor: 32,
                    revision: 33,
                    buildNumber: 34,
                },
                clrOverride: {
                    installRoot: p`COMPLUS_InstallRootLocaiton`,
                    version: "41.42.43.44",
                    defaultVersion: "51.52.53.54",
                    guiFromShim: true,
                    dbgJitDebugLaunchSetting: true,
                    onlyUseLatestClr: true,
                },
            },
        };
        Transformer.execute({
            tool: tool,
            workingDirectory: d`out/working`,
            implicitOutputs: [p`out/file1.out`],
            arguments: [],
        });
    }
    
    @@Testing.unitTest()
    export function allArgumentTypes() {
        Transformer.execute({
            tool: {exe: f`src/stringInput.exe`},
            workingDirectory: d`out/working`,
            implicitOutputs: [p`out/file1.out`],
            arguments: [
                {name: "argStr", value: "strValue"},
                {name: "argNum", value: 123},
                {
                    name: "argAtom",
                    value: `atomValue`,
                },
                
                {name: "argArtifactPathNone", value: {path: p`src/artifactPathNone`, kind: ArtifactKind.none}},
                {
                    name: "argArtifactPathIn",
                    value: {path: p`src/artifactPathIn`, kind: ArtifactKind.input},
                },
                {name: "argArtifactPathOut", value: {path: p`out/artifactPathOut`, kind: ArtifactKind.output}},
                //{ name: "argArtifactPathRewr", value: {path: p`out/artifactPathRewr`, kind: ArtifactKind.rewritten}}, // TODO: Fails with assert today, should handle better.
                
                {name: "argArtifactFileNone", value: {path: f`src/artifactFileNone`, kind: ArtifactKind.none}},
                {
                    name: "argArtifactFileIn",
                    value: {path: f`src/artifactFileIn`, kind: ArtifactKind.input},
                },
                //{ name: "argArtifactFileOut", value: {path: f`out/artifactFileOut`, kind: ArtifactKind.output}}, // TODO: Fails with assert today, should handle better.
                //{ name: "argArtifactFileRewr", value: {path: Transformer.copyFile(f`src/artifactFileRewr`, p`out/artifactFileRewr`), kind: ArtifactKind.rewritten}}, // TODO: Fails with assert today, should handle better.
                
                {name: "argArtifactDirNone", value: {path: d`src/artifactDirNone`, kind: ArtifactKind.none}},
                //{ name: "argArtifactDirIn", value: {path: d`src/artifactDirIn`, kind: ArtifactKind.input}}, // TODO: Fails with assert today, should handle better.
                {
                    name: "argArtifactDirOut",
                    value: {path: d`out/artifactDirOut`, kind: ArtifactKind.output},
                },
                //{ name: "argArtifactDirRewr", value: {path: d`out/artifactDirRewr`, kind: ArtifactKind.rewritten}}, // TODO: Fails with assert today, should handle better.
                
                {
                    name: "argArtifactSDirNone",
                    value: {
                        path: Transformer.sealPartialDirectory(d`src/artifactDirSNone`, []),
                        kind: ArtifactKind.none,
                    },
                },
                {
                    name: "argArtifactSDirIn",
                    value: {
                        path: Transformer.sealPartialDirectory(d`src/artifactDirSIn`, []),
                        kind: ArtifactKind.input,
                    },
                },
                //{ name: "argArtifactSDirOut", value: {path: Transformer.sealDirectory(d`out/artifactDirSOut`, []), kind: ArtifactKind.output}}, // TODO: Fails with long error about this dir writing into itself
                //{ name: "argArtifactSDirRewr", value: {path: Transformer.sealDirectory(d`out/artifactDirSRewr`, []), kind: ArtifactKind.rewritten}}, // TODO: Fails with assert today, should handle better.
                
                {name: "argPrimaryStrRaw", value: {value: "argPrimaryStrRaw", kind: ArgumentKind.rawText}},
                {
                    name: "argPrimaryStrRegular",
                    value: {value: "argPrimaryStrRaw", kind: ArgumentKind.regular},
                },
                {name: "argPrimaryStrFlag", value: {value: "argPrimaryStrRaw", kind: ArgumentKind.flag}},
                {
                    name: "argPrimaryStrRsp",
                    value: {value: "argPrimaryStrRaw", kind: ArgumentKind.startUsingResponseFile},
                },
                
                {name: "argPrimaryNumRaw", value: {value: 1234, kind: ArgumentKind.rawText}},
                {name: "argPrimaryNumRegular", value: {value: 1235, kind: ArgumentKind.regular}},
                {name: "argPrimaryNumFlag", value: {value: 1236, kind: ArgumentKind.flag}},
                {name: "argPrimaryNumRsp", value: {value: 1237, kind: ArgumentKind.startUsingResponseFile}},
                
                {name: "argPrimaryAtomRaw", value: {value: a`argPrimaryAtomRaw`, kind: ArgumentKind.rawText}},
                {
                    name: "argPrimaryAtomRegular",
                    value: {value: a`argPrimaryAtomRaw`, kind: ArgumentKind.regular},
                },
                {name: "argPrimaryAtomFlag", value: {value: a`argPrimaryAtomRaw`, kind: ArgumentKind.flag}},
                {
                    name: "argPrimaryAtomRsp",
                    value: {value: a`argPrimaryAtomRaw`, kind: ArgumentKind.startUsingResponseFile},
                },
                
                {
                    name: "compound",
                    value: {
                        values: [
                            "compoundStrValue",
                            123,
                            `compoundAtomValue`,
                            {path: f`src/compoundFileIn`, kind: ArtifactKind.input},
                        ],
                        separator: "-compoundSeparator-",
                    },
                },
                
                {name: "valuesStr", value: ["valuesStrValue1", "valueStrValue2"]},
                {
                    name: "valuesInputArtifact",
                    value: [{path: f`src/valuesFileIn1`, kind: ArtifactKind.input}, {path: f`src/valuesFileIn2`, kind: ArtifactKind.input}],
                },
                
                {value: "noNameString"},
            ],
        });
    }

    @@Testing.unitTest()
    export function executeAllOutputTypes() {
        const result = Transformer.execute({
            tool: mostFields,
            description: "testDescription",
            arguments: [],
            workingDirectory: d`out/working`,
            dependencies: [f`src/input/file.txt`],
            outputs: [
                f`out/outputFile1.txt`,
                p`out/outputFile2.txt`,
                d`out/Directory1`,
                {kind: "exclusive", directory: d`out/Directory2`},
                {kind: "shared", directory: d`out/Directory3`},
                {existence: "required", artifact: p`out/outputFile3.txt`},
                {existence: "optional", artifact: p`out/outputFile4.txt`},
                {existence: "temporary", artifact: p`out/outputFile5.txt`},
            ]
        });
        
        [   d`out/Directory1`, 
            d`out/Directory2`, 
            d`out/Directory3`
        ].all(directory => 
            {
                Assert.areEqual(
                    directory.path,  
                    result.getOutputDirectory(directory).root.path);
                return true;
            });

        const pathToFile1 = f`out/outputFile1.txt`.path;
        Assert.areEqual(pathToFile1, result.getOutputFile(pathToFile1).path);
        
        [   p`out/outputFile2.txt`, 
            p`out/outputFile3.txt`, 
            p`out/outputFile4.txt`,
            p`out/outputFile5.txt`
        ].all(artifact => 
        {
            Assert.areEqual(
                artifact.path,  
                result.getOutputFile(artifact).path);
            return true;
        });
    }
    
    @@Testing.unitTest()
    export function executeWithAllowedUndeclaredReads() {
        const result = Transformer.execute({
            tool: mostFields,
            description: "executeWithAllowedUndeclaredReads",
            arguments: [],
            workingDirectory: d`out/working`,
            dependencies: [],
            outputs: [
                f`out/outputFile1.txt`,
            ],
            allowUndeclaredSourceReads: true,
        });
    }

    @@Testing.unitTest()
    export function executeWithAllowedSurvivingChildProcess() {
        const result = Transformer.execute({
            tool: mostFields,
            description: "executeWithAllowedSurvivingChildProcess",
            arguments: [],
            workingDirectory: d`out/working`,
            dependencies: [],
            outputs: [
                f`out/outputFile1.txt`,
            ],
            allowedSurvivingChildProcessNames: [ a`childAsPathAtom.exe`, "childAsString.exe" ],
            nestedProcessTerminationTimeoutMs: 5000,
        });
    }
}

