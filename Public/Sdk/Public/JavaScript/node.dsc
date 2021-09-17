// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

namespace Node {
    /**
     * Returns a static directory containing a valid Node installation.
     * This function requires Node installed locally
     */
    @@public
    export function getDefaultNodeInstallation() : StaticDirectory {
        const installedNodeLocation = d`${Context.getMount("ProgramFiles").path}/nodejs`;
        const nodeExe = f`${installedNodeLocation}/node.exe`;
        if (!File.exists(nodeExe))
        {
            Contract.fail(`Could not find Node installed. File '${nodeExe.toDiagnosticString()}' does not exist.`);
        }

        return Transformer.sealDirectory(installedNodeLocation, globR(installedNodeLocation));
    }

    /**
     * Returns a Transformer.ToolDefinition for node.
     * If the a node installation is not provided, getDefaultNodeInstallation() is used to find a locally installed one
     * If the relative path to node is not provided, the first occurrence of node executable in the installation is used
     */
    @@public
    export function getNodeTool(nodeInstallation?: StaticDirectory, relativePathToInstallation?: RelativePath) : Transformer.ToolDefinition {
        nodeInstallation = nodeInstallation || getDefaultNodeInstallation();

        let nodeToolFile = undefined;
        // If the specific location of node is not provided, try to find it under the static directory
        if (!relativePathToInstallation) {
            let nodeToolName = Context.isWindowsOS? a`node.exe` : a`node`;
            let nodeToolFound = nodeInstallation.contents.find((file, index, array) => array[index].name === nodeToolName);
            if (nodeToolFound !== undefined) {
                nodeToolFile = nodeToolFound;
            }
            else {
                Contract.fail(`Could not find Node under the provided node installation.`);
            }
        }
        else {
            // Otherwise, just get it from the static directory as specified
            nodeToolFile = nodeInstallation.assertExistence(relativePathToInstallation);
        }
        
        return {
            exe: nodeToolFile,
            dependsOnWindowsDirectories: true,
            dependsOnCurrentHostOSDirectories: true,
            dependsOnAppDataDirectory: true,
            runtimeDirectoryDependencies: [nodeInstallation],
            prepareTempDirectory: true};
    }
}