// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * Validates the hash of the input file. It returns a copy only if the hash is correct.
 * This is so that downstream tools can decide to wait for the verification (by using the copy)
 * or not (by using the original file as a dependency).
 */
@@public
export function validateFileHash(file: File, hash: string, algorithm?: "SHA1"|"SHA256"|"SHA384"|"SHA512"|"MD5") : File {
    const destination = p`${Context.getNewOutputDirectory("ValidateFileHash")}/${file.name}`;

    const result = executeCommands([
            "$hashFromFile = Get-FileHash -Path $Env:Param_InFile -Algorithm $Env:Param_Algorithm -Verbose",
            "$hashValue = $hashFromFile.Hash",
            "if ($hashValue -ne $Env:Param_Hash) { Write-Error \"\\n\\nEncountered wrong hash for file: '$Env:Param_InFile'. Expected hash: '$Env:Param_Hash' but encountered hash: '$hashValue'\"; exit 1 }",
            "Copy-Item $Env:Param_InFile $Env:Param_OutFile"
        ],
        {
            environmentVariables: [
                {name: "Param_InFile", value: file },
                {name: "Param_OutFile", value: destination },
                {name: "Param_Hash", value: hash },
                {name: "Param_Algorithm", value: algorithm || "SHA256" },
            ],
            dependencies: [
                file,
            ],
            outputs: [
                destination,
            ],
        });

    return result.getOutputFile(destination);
}
