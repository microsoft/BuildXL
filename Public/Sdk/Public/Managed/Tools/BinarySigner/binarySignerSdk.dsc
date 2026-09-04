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

interface ESRPSignRequest {
    source: File;
    destination: File;
}

/**
 * Signs all dll and exe files from one coherently produced sealed directory in a single ESRP invocation,
 * and copies the remaining files into the signed directory without combining files from unrelated producers.
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
    // Sort by path so directory enumeration order does not affect the ESRP request JSON or the signing pip fingerprint.
    const fileList = sealedDir.contents.toSet().sort((left, right) => left.path.toString().localeCompare(right.path.toString()));
    const signRequests = fileList
        .filter(file => file.extension === a`.dll` || file.extension === a`.exe`)
        .map(file => {
            const source = sealedDir.getFile(file.path);
            const relativePath = sealedDirPath.getRelative(file.path);
            return {
                source: source,
                destination: f`${signedDir.path}/${relativePath}`,
            };
        });
    const copiedFiles = fileList
        .filter(file => file.extension !== a`.dll` && file.extension !== a`.exe`)
        .map(file => {
            const relativePath = sealedDirPath.getRelative(file.path);
            return Transformer.copyFile(sealedDir.getFile(file.path), p`${signedDir.path}/${relativePath}`);
        });
    const signedFiles = signRequests.length === 0
        ? []
        : signFiles(
            esrpSignConfiguration,
            signRequests,
            sealedDir.path,
            signedDir.path,
            `ESRP Signing ${signRequests.length} files to ${signedDir.path}`);

    return [...signedFiles, ...copiedFiles];
}

/**
 * Signs one independently produced binary and returns the signed output.
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
    const outputDirectory = args.outputDir === undefined ? Context.getNewOutputDirectory("esrpSignOutput") : args.outputDir;
    const signedFile = f`${outputDirectory.path}/${args.file.name}`;
    return signFiles(
        args,
        [{source: args.file, destination: signedFile}],
        args.file.parent,
        outputDirectory.path,
        `ESRP Signing ${args.file.name}`)[0];
}

/**
 * Schedules one ESRP process whose declared dependencies and outputs cover every request in the batch.
 */
function signFiles(
    esrpSignConfiguration: EsrpSignConfiguration,
    signRequests: ESRPSignRequest[],
    sourceRoot: Path,
    destinationRoot: Path,
    description: string): File[] {
    const consoleOutputDirectory = Context.getNewOutputDirectory("esrpSignConsoleOutput");
    const fileListJson = p`${consoleOutputDirectory}/bxlEsrpBinarySignerSdk.json`;
    const jsonFile = createFileListJsonForSigning(signRequests, sourceRoot, destinationRoot, fileListJson);
    const exeArgs : Transformer.ExecuteArguments = {
            tool: {
                exe: f`${esrpSignConfiguration.signToolPath}`,
                untrackedDirectoryScopes: [
                        ...(Context.getCurrentHost().os === "win" ? [
                            d`${Context.getMount("ProgramData").path}`,
                            d`${Context.getMount("ProgramFiles").path}/dotnet`,
                            d`${Context.getMount("UserProfile").path}`
                        ] : [])
                    ],
                runtimeDependencies: globR(d`${esrpSignConfiguration.signToolPath.parent.path}`, "*"),
                prepareTempDirectory: true,
                dependsOnAppDataDirectory: true,
                dependsOnCurrentHostOSDirectories: true,
            },
            description: description,
            arguments: [
                Cmd.argument("sign"),
                Cmd.option("-i ", Artifact.input(jsonFile)),
                Cmd.option("-c ", Artifact.input(f`${esrpSignConfiguration.signToolConfiguration}`)),
                Cmd.option("-p ", Artifact.input(f`${esrpSignConfiguration.signToolEsrpPolicy}`)),
                Cmd.option("-l ", "Error")
            ],
            dependencies: [
                ...signRequests.map(request => request.source),
                f`${esrpSignConfiguration.signToolAadAuth}`,
                f`${esrpSignConfiguration.signToolEsrpPolicy}`,
                f`${esrpSignConfiguration.signToolConfiguration}`
            ],
            outputs: signRequests.map(request => request.destination),
            consoleOutput: p`${consoleOutputDirectory}/prssSign.log`,
            tempDirectory: Context.getTempDirectory("esrpSignTemp"),
            workingDirectory: consoleOutputDirectory
    };

    const result = Transformer.execute(exeArgs);
    return signRequests.map(request => <File>result.getOutputFile(request.destination.path));
}

function createFileListJsonForSigning(signRequests: ESRPSignRequest[], sourceRoot: Path, destinationRoot: Path, fileListJsonPath: Path): File {
    const jsonText = {
        "Version": "1.0.0",
        "SignBatches" : [
            {
                "SourceLocationType": "UNC",
                "SourceRootDirectory": sourceRoot,
                "DestinationLocationType": "UNC",
                "DestinationRootDirectory": destinationRoot,
                "SignRequestFiles": signRequests.map(request => ({
                    "SourceLocation": request.source.path,
                    "DestinationLocation": request.destination.path
                })),
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