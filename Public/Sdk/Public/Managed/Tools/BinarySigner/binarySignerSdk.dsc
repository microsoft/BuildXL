// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Json from "Sdk.Json";

/**
 * ESRP Signer arguments
 */
@@public
export interface ESRPSignArguments extends EsrpSignConfiguration{

    /** Original file to be signed. */
    file: File;

    /** Output Directory. binarySignerSdk will create a new output directory if it's not provided*/
    outputDir?: Path;
}

/**
 * Given a sealed directory, sign the dll adn exe binary files and copy other files into signed directory.
 */
@@public
export function signDirectory(esrpSignConfiguration: EsrpSignConfiguration, sealedDir: StaticContentDirectory, signedDir: Directory) : File[] {
    Contract.requires(
        sealedDir !== undefined,
        `Binary Signing was called for undefined files or directory.`
    );

    Contract.requires(
        esrpSignConfiguration.signToolPath !== undefined,
        `Binary Signing was called for an undefied tool. EsrpSignConfiguration: ${esrpSignConfiguration}`
    );

    const sealedDirPath = sealedDir.path;
    // Deduplicate the files. Nuget packages may contain duplicate entries for the same file if their zip central directory was not built properly.
    const fileList = sealedDir.contents.toSet();
    // Sign the dll and exe binary files and copy other files into signed directory.
    let signedFiles : File[] = fileList.map(originalFile => {
        let filePath = originalFile.path;
        let relativePath = sealedDirPath.getRelative(filePath);
        let f : File = sealedDir.getFile(filePath);
        let outputFilePath= p`${signedDir.path}/${relativePath}`;
        if (f.extension === a`.dll` || f.extension === a`.exe`) {
            let newSignArgs = esrpSignConfiguration.merge<ESRPSignArguments>({
                file: f,
                outputDir: d`${outputFilePath.parent}`,
            });

            return signBinary(newSignArgs);
        }

        return Transformer.copyFile(f, outputFilePath);
    });

    return signedFiles;
}

/**
 * Returns a new file for given binary file
 */
@@public
export function signBinary(args: ESRPSignArguments): File {
    Contract.requires(
        args.file !== undefined,
        `Binary Signing was called for an undefined file. ESRPSignArguments: ${args}`
    );

    Contract.requires(
        args.signToolPath !== undefined,
        `Binary Signing was called for an undefied tool. ESRPSignArguments: ${args}`
    );
    
    let outputDirectory = args.outputDir === undefined ? Context.getNewOutputDirectory("esrpSignOutput") : args.outputDir;
    let consoleOutputDirectory = Context.getNewOutputDirectory("esrpSignConsoleOutput");
    let fileListJson = p`${consoleOutputDirectory}/bxlEsrpBinarySignerSdk.json`;

    let signedFile = f`${outputDirectory.path}/${args.file.name}`;   // Final Output: Signed version of given file

    let jsonFile = createFileListJsonForSigning(args.file, signedFile, fileListJson);

    const exeArgs : Transformer.ExecuteArguments = {
            tool: { 
                exe: f`${args.signToolPath}`,
                untrackedDirectoryScopes: [
                        ...(Context.getCurrentHost().os === "win" ? [
                            d`${Context.getMount("ProgramData").path}`,
                            d`${Context.getMount("UserProfile").path}`
                        ] : [])
                    ],
                runtimeDependencies: globR(d`${args.signToolPath.parent.path}`, "*"),
                prepareTempDirectory: true,
                dependsOnAppDataDirectory: true,
                dependsOnCurrentHostOSDirectories: true,
            },
            description: `ESRP Signing ${args.file.name}`,
            arguments: [
                Cmd.argument("sign"),
                Cmd.option("-i ", Artifact.input(jsonFile)),
                Cmd.option("-c ", Artifact.input(f`${args.signToolConfiguration}`)),
                Cmd.option("-p ", Artifact.input(f`${args.signToolEsrpPolicy}`)),
                Cmd.option("-l ", "Verbose")
            ],
            dependencies: [
                args.file,
                f`${args.signToolAadAuth}`,
                f`${args.signToolEsrpPolicy}`,
                f`${args.signToolConfiguration}`
            ],
            outputs: [
                signedFile
            ],
            consoleOutput: p`${consoleOutputDirectory}/prssSign.log`,
            tempDirectory: Context.getTempDirectory("esrpSignTemp"),
            workingDirectory: consoleOutputDirectory
    };

    let result = Transformer.execute(exeArgs);
    return <File>result.getOutputFile(signedFile.path);
}

function createFileListJsonForSigning(input: File, output: File, fileListJsonPath: Path): File {   
    let jsonText = {
        "Version": "1.0.0",
        "SignBatches" : [
            {
                "SourceLocationType": "UNC", 
                "SourceRootDirectory": p`${input.parent.path}`,
                "DestinationLocationType": "UNC",
                "DestinationRootDirectory": p`${output.parent.path}`,
                "SignRequestFiles": [{
                    "SourceLocation": p`${input.path}`,
                    "DestinationLocation": p`${output.path}`
                }],
                "SigningInfo": {
                    "Operations": [
                        {
                            "KeyCode": "CP-230856",
                            "OperationCode": "SigntoolSign",
                            "Parameters": 
                            {
                                "OpusName": "Microsoft",
                                "OpusInfo": "http://www.microsoft.com",
                                "FileDigest": "/fd \"SHA256\"",
                                "PageHash": "/NPH",
                                "TimeStamp": "/tr \"http://rfc3161.gtm.corp.microsoft.com/TSS/HttpTspServer\" /td sha256"
                            },
                            "ToolName": "sign",
                            "ToolVersion": "1.0"
                        },
                        {
                            "KeyCode": "CP-230856",
                            "OperationCode": "SigntoolVerify",
                            "Parameters": {},
                            "ToolName": "sign",
                            "ToolVersion": "1.0"
                        }
                    ]
                }
            }
        ]
    };

    const options : Json.AdditionalJsonOptions = {
        pathRenderingOption: Context.getCurrentHost().os !== "win" ? "escapedBackSlashes" : "forwardSlashes"
    };

    return Json.write(fileListJsonPath, jsonText, '"', [], "ESRP Sign Info Json", options);
}