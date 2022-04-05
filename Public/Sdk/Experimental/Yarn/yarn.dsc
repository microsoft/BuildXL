// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * Returns a static directory containing a valid Yarn installation.
 */
@@public
export function getYarn() : StaticDirectory {
    if (Environment.getFlag("[Sdk.BuildXL]microsoftInternal")) {
        // Internally in Microsoft we use a nuget package that contains Yarn.
        return Transformer.reSealPartialDirectory(importFrom("NPM.OnCloudbuild").Contents.all, r`tools/Yarn`);
    }

    // For the public build, we require Yarn to be installed
    const installedYarnLocation = d`${Context.getMount("ProgramFilesX86").path}/Yarn`;
    const packageJson = f`${installedYarnLocation}/package.json`;
    if (!File.exists(packageJson))
    {
        Contract.fail(`Could not find Yarn installed. File '${packageJson.toDiagnosticString()}' does not exist.`);
    }

    return Transformer.sealDirectory(installedYarnLocation, globR(installedYarnLocation));
}