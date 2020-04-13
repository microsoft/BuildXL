// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import {Node, Npm} from "Sdk.NodeJs";

namespace RushGraphBuilder {
    export declare const qualifier: {};

    // Copy the sources to an output directory so we don't polute the source tree with outputs
    const rushToolSrc = Transformer.sealDirectory(d`src`, globR(d`src`));
    const outputDir = Context.getNewOutputDirectory("rush-graph-builder-copy");
    const rushSrcCopy: SharedOpaqueDirectory = Deployment.copyDirectory(
        rushToolSrc.root, 
        outputDir, 
        rushToolSrc);

    // Install required npm packages
    const npmInstall = Npm.npmInstall(rushSrcCopy, []);

    // Compile
    const compileOutDir: SharedOpaqueDirectory = Node.tscCompile(
        rushSrcCopy.root, 
        [ rushSrcCopy, npmInstall ]);

    const outDir = Transformer.composeSharedOpaqueDirectories(
        outputDir, 
        [compileOutDir]);

    const nodeModules = Deployment.createDeployableOpaqueSubDirectory(npmInstall, r`node_modules`);
    const out = Deployment.createDeployableOpaqueSubDirectory(outDir, r`out`);

    // The deployment also needs all node_modules folder that npm installed
    // This is the final layout the tool needs
    const privateDeployment : Deployment.Definition = {
        contents: [
            out,
            {
                contents: [{subfolder: `node_modules`, contents: [nodeModules]}]
            }
        ]
    };

    // Unfortunately, drop service does not handle opaque subdirectories, so we need to create a single shared opaque
    // that contains the full layout by copying
    const sourceDeployment : Directory = Context.getNewOutputDirectory("rush-tool-deployment");
    const onDiskDeployment = Deployment.deployToDisk({definition: privateDeployment, targetDirectory: sourceDeployment, sealPartialWithoutScrubbing: true});

    const finalOutput : SharedOpaqueDirectory = Deployment.copyDirectory(
        sourceDeployment, 
        Context.getNewOutputDirectory("rush-tool-final-deployment"),
        onDiskDeployment.contents,
        onDiskDeployment.targetOpaques);

    @@public export const deployment : Deployment.Definition = {
        contents: [{
            subfolder: r`tools/RushGraphBuilder`,
            contents: [finalOutput]
        }]
    };
}
