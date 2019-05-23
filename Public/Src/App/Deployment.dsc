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

function createDeploymentManifest(isServerDeployment: boolean) : Deployment.Definition {
    return {
        contents: [
            // Use the operating system specific BuildXL binary for deployments
            Main.exe,
            f`DefaultCacheConfig.json`,

            ...addIfLazy(!BuildXLSdk.isDotNetCoreBuild, () => [
                importFrom("BuildXL.Cache.MemoizationStore").deploymentForBuildXL
            ]),

            importFrom("BuildXL.Cache.VerticalStore").Deployment.deployment,
            importFrom("BuildXL.Cache.ContentStore").deploymentForBuildXL,

            ...addIfLazy(qualifier.targetRuntime === "win-x64", () => [
                RunInSubst.withQualifier({configuration: qualifier.configuration, platform: "x86"}).deployment,
                DetoursServices.Deployment.definition
            ]),

            ...addIfLazy(MacServices.Deployment.macBinaryUsage !== "none" && qualifier.targetRuntime === "osx-x64", () => [
                MacServices.Deployment.kext,
                MacServices.Deployment.sandboxMonitor,
                MacServices.Deployment.ariaLibrary,
                MacServices.Deployment.interopLibrary,
                MacServices.Deployment.buildXLScripts,
                MacServices.Deployment.sandboxLoadScripts
            ]),

            isServerDeployment ? inBoxServerSdks : inBoxSdks
        ],
    };
}

/**
 * Both manifest names are defined in BuildXL.Engine\AppDeployment.cs,
 * the manifestName parameter of addDeploymentManifestFile must match either of those string constants!
 */
@@public
export const deployment = addDeploymentManifestFile(createDeploymentManifest(false), "BuildXL.Deployment.manifest");

/** This manifest file gets processed by the logic in App\Bxl\ServerDeployment.cs */
@@public
export const serverDeployment = addDeploymentManifestFile(createDeploymentManifest(true), "BuildXL.ServerDeployment.manifest");
