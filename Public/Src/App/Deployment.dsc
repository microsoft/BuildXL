// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as MacServices from "BuildXL.Sandbox.MacOS";

@@public
export function addDeploymentManifestFile(deployment: Deployment.Definition, manifestName: string): Deployment.Definition {
    let lines: RelativePath[] = Deployment.extractRelativePaths(deployment).map(
        item => item[0]
    );
    const manifest = Transformer.writeAllLines({
        outputPath: Context.getNewOutputDirectory("deploymentManifest").combine(manifestName),
        lines: lines
    });

    return {contents: [deployment, manifest]};
}

function createDeployment(isServerDeployment: boolean, isMinimal: boolean) : Deployment.Definition {
    return {
        contents: [
            // Use the operating system specific BuildXL binary for deployments
            Main.exe,
            f`DefaultCacheConfig.json`,
            
            importFrom("BuildXL.Tools").AdoBuildRunner.deployment,
            importFrom("BuildXL.Cache.VerticalStore").Deployment.deployment,
            importFrom("BuildXL.Cache.ContentStore").deploymentForBuildXL,

            ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [
                RunInSubst.withQualifier({platform: "x86"}).deployment,
            ]),

            ...addIfLazy(qualifier.targetRuntime === "osx-x64" || 
                         qualifier.targetRuntime === "linux-x64", () => [
                MacServices.Deployment.buildXLScripts,
            ]),

            isServerDeployment ? inBoxServerSdks : inBoxSdks(isMinimal)
        ],
    };
}

/**
 * Both manifest names are defined in BuildXL.Engine\AppDeployment.cs,
 * the manifestName parameter of addDeploymentManifestFile must match either of those string constants!
 */
@@public
export const deployment = addDeploymentManifestFile(createDeployment(false, false), "BuildXL.Deployment.manifest");

/** This manifest file gets processed by the logic in App\Bxl\ServerDeployment.cs */
@@public
export const serverDeployment = addDeploymentManifestFile(createDeployment(true, false), "BuildXL.ServerDeployment.manifest");

/** 
 * Minimal deployment meant for local builds, does not deploy daemon sdks/tooling.
 * The intention is to minimize package size of the distribution 
 * (specifically, to avoid hitting npm package size limitations)
 */
@@public
export const minimalDeployment = addDeploymentManifestFile(createDeployment(false, true), "BuildXL.Deployment.manifest");
