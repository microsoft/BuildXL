// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Json from "Sdk.Json";

/**
 * Arguments for the 'ESRPClient sign' operation.
 */
@@public
export interface SignFileInfo extends Transformer.RunnerArguments {
    /** Original file to be signed. */
    file: File;

    /** ESRP session information config, ESRPClient's -c argument */
    signToolConfiguration: File;

    /** ESRP policy information config, ESRPClient's -p argument */
    signToolEsrpPolicy: File;

    /** EsrpAuthentication.json */
    signToolAadAuth : File;
}

/**
 * Process ESRP Sign requirements
 */
@@public
export const esrpSignFileInfoTemplate: SignFileInfo = Environment.getFlag("ENABLE_ESRP") ? {
    tool: { 
        exe : f`${Environment.expandEnvironmentVariablesInString(Environment.getStringValue("SIGN_TOOL_PATH"))}`,
        untrackedDirectoryScopes: [
            ...(Context.getCurrentHost().os === "win" ? [
                d`${Context.getMount("ProgramData").path}`,
                d`${Context.getMount("UserProfile").path}`
            ] : [])
        ],
        runtimeDependencies : globR(d`${Context.getMount("ESRPClientRoot").path}`, "*"),
    },
    file: undefined,
    signToolConfiguration: Environment.getFileValue("ESRP_SESSION_CONFIG"),
    signToolEsrpPolicy: Environment.getFileValue("ESRP_POLICY_CONFIG"),
    signToolAadAuth: f`${Context.getMount("SourceRoot").path}/Secrets/CodeSign/EsrpAuthentication.json`,
} : undefined;

/**
 * Returns a new file for given binary file
 */
@@public
export function signBinary(signInfo: SignFileInfo, outputDir?: Directory): File {
    Contract.requires(
        Environment.getFlag("ENABLE_ESRP") === true,
        "Environment Flag ENABLE_ESRP not set, but Binary Signing was called."
    );

    Contract.requires(
        signInfo.file !== undefined,
        `Binary Signing was called for an undefined file. SignInfo: ${signInfo}`
    );
    
    if (signInfo.tool === undefined) {
        signInfo = signInfo.override<SignFileInfo>(esrpSignFileInfoTemplate);
    }

    let outputDirectory = outputDir === undefined ? Context.getNewOutputDirectory("esrpSignOutput") : outputDir;
    let fileListJson = p`${outputDirectory}/bxlEsrpBinarySignerSdk.json`;

    let signedFile = f`${outputDirectory.path}/${signInfo.file.name}`;   // Final Output: Signed version of given file

    let jsonFile = createFileListJsonForSigning(signInfo.file, signedFile, fileListJson);

    const exeArgs = Object.merge<Transformer.ExecuteArguments>(
        {
            description: `ESRP Signing ${signInfo.file.name}`,
            arguments: [
                Cmd.argument("sign"),
                Cmd.option("-i ", Artifact.input(jsonFile)),
                Cmd.option("-c ", Artifact.input(signInfo.signToolConfiguration)),
                Cmd.option("-p ", Artifact.input(signInfo.signToolEsrpPolicy)),
                Cmd.option("-l ", "Verbose")
            ],
            dependencies: [
                signInfo.file,
                signInfo.signToolAadAuth
            ],
            outputs: [
                signedFile
            ],
            prepareTempDirectory: true,
            dependsOnWindowsDirectories: true,
            dependsOnAppDataDirectory: true,
            dependsOnCurrentHostOSDirectories: true,
            consoleOutput: p`${outputDirectory}/prssSign.log`,
            tempDirectory: Context.getTempDirectory("esrpSignTemp"),
            workingDirectory: outputDirectory
        },
        signInfo
    );

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
                            "KeyCode": "CP-230072",
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
                            "KeyCode": "CP-230072",
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