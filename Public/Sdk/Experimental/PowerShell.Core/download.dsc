// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

@@public
export function downloadFile(args: DownloadArgs) : DownloadResult {
    let downloadLocation = p`${Context.getNewOutputDirectory('download')}/${args.fileName}`;

    const result = executeCommands([
            "Microsoft.PowerShell.Utility\\Invoke-Webrequest -Uri $Env:Param_Uri -OutFile $Env:Param_OutFile -Verbose",
        ],
        {
            environmentVariables: [
                {name: "Param_Uri", value: args.url },
                {name: "Param_OutFile", value: downloadLocation },
            ],
            outputs: [
                downloadLocation,
            ],
        });

    let resultFile : File = result.getOutputFile(downloadLocation);

    if (args.sha256Hash) {
        // we use the copied validated file so that we don't run any pips untill the hash is validated
        resultFile = validateFileHash(resultFile, args.sha256Hash, "SHA256");
    }

    return {
        file: resultFile,
    };
}

export interface DownloadArgs {
    url: string,
    fileName: string | PathAtom,
    sha256Hash: string,
}

export interface DownloadResult {
    file: File,
}
