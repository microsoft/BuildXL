// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * This should be a supported primitive in BuildXL, but it is not so we have to hack here
 */
@@public 
export function extractFileFromOpaque(dir: OpaqueDirectory, fileLocation: RelativePath) : File {
    const fromPath = p`${dir.root}/${fileLocation}`;
    const toPath = p`${Context.getNewOutputDirectory("extractSealDir")}/${fromPath.name}`;

    const result = executeCommands([
        "Copy-Item $Env:Param_From -Destination $Env:Param_To",
    ], {
        environmentVariables: [
            {name: "Param_From", value: fromPath },
            {name: "Param_To", value: toPath },
        ],
        dependencies: [
            dir,
        ],
        outputs: [
            toPath,
        ],
    });

    return result.getOutputFile(toPath);
}
