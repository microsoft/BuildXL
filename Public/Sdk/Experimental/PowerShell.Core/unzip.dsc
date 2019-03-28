// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * Unzips a file to an opaque output folder
 */
@@public
export function unZip(args: UnZipArguments) : UnZipResult {

    const destination = args.destination || Context.getNewOutputDirectory("unzip");

    const result = executeCommands([
            "Microsoft.PowerShell.Archive\\Expand-Archive $Env:Param_ZipFile -DestinationPath $Env:Param_Destination -Verbose",
        ],
        {
            environmentVariables: [
                {name: "Param_ZipFile", value: args.zipFile },
                {name: "Param_Destination", value: destination },
            ],
            dependencies: [
                args.zipFile,
            ],
            outputs: [
                destination,
            ],
        });

    return {
        directory: result.getOutputDirectory(destination),
    };
}

@@public
export interface UnZipArguments {
    zipFile: File,
    destination?: Directory,
}

@@public 
export interface UnZipResult {
    directory: OpaqueDirectory,
}
