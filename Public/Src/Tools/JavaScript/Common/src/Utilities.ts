// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as path from "path";
import * as fs from "fs";

export function getErrorFileDescriptor(outputGraphFile: string) : number {

        // A file with the same name as the output graph file but with a .err extension
        // will be picked up on managed side and displayed to the user in case of errors.
        const outputDirectory = path.dirname(outputGraphFile);
        const errorFileName = `${path.basename(outputGraphFile)}.err`;
        const errorFd = fs.openSync(path.join(outputDirectory, errorFileName), 'w')

        return errorFd;
}