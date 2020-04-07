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

    // The final deployment also needs all node_modules folder that npm installed
    @@public
    export const deployment : Deployment.Definition = {contents: [{
        subfolder: r`tools/RushGraphBuilder`,
        contents: [
            out,
            {
                contents: [{subfolder: `node_modules`, contents: [nodeModules]}]
            }
        ]
    }]};
}
