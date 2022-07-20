// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

// Yarn tgz sadly includes the version as a top level directory
const yarnVersion = "yarn-v1.22.19";

/**
 * Returns a static directory containing a valid Yarn installation.
 */
@@public
export function getYarn() : StaticDirectory {
    const yarnPackage = importFrom("YarnTool").yarnPackage;

    const yarnDir = Context.getNewOutputDirectory("yarn-package");
    const isWinOs = Context.getCurrentHost().os === "win";

    // 'yarn', the linux executable that comes with the package, doesn't have the execution permissions set 
    let executableYarn = undefined;
    if (!isWinOs) {
        const yarnExe = yarnPackage.assertExistence(r`${yarnVersion}/bin/yarn`);
        executableYarn = Transformer.makeExecutable(yarnExe, p`${yarnDir}/${yarnVersion}/bin/yarn`);
    }

    const sharedOpaqueYarnPackage = Transformer.copyDirectory({
        sourceDir: yarnPackage.root, 
        targetDir: yarnDir, 
        dependencies: [yarnPackage, executableYarn], 
        recursive: true, 
        excludePattern: isWinOs? undefined : "yarn"});

    const finalDeployment = Context.getNewOutputDirectory("yarn-package-final");
    
    const result = Transformer.copyDirectory({
        sourceDir: d`${sharedOpaqueYarnPackage.root}/${yarnVersion}`, 
        targetDir: finalDeployment, 
        dependencies: [sharedOpaqueYarnPackage, executableYarn], 
        recursive: true});

    return result;
}