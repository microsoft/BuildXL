// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as ManagedSdk from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import * as Transformers from "Sdk.Transformers";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Branding from "BuildXL.Branding";
import * as VSIntegration from "BuildXL.Ide.VsIntegration";

namespace LanguageService.Server {

    /**
     * Builds a VSIX for given version that packages the server assembly (with closure of its references)
     * as well as client resources
     */
    export function buildVsix(serverAssembly: ManagedSdk.Assembly) : DerivedFile {

        const vsixDeploymentDefinition = buildVsixDeploymentDefinition(serverAssembly);

        // Special "scrubbable" mount should be use for deploying vsix package content.
        // This is done to avoid "dangling" file issues. If the file was removed from the deployment
        // it still may be in the output folder. And in this case BuildXL will fail with file mon violation.
        const targetDirectory = d`${Context.getMount("ScrubbableDeployment").path}/${qualifier.configuration}/${qualifier.targetFramework}/${qualifier.targetRuntime}/VsCodeVsix`;

        const vsixDeployment = Deployment.deployToDisk({
            definition: vsixDeploymentDefinition,
            targetDirectory: targetDirectory,
        });

        // Zip and return
        const vsix = VSIntegration.Tool.CreateZipPackage.run({
            outputFileName: `BuildXL.vscode.${qualifier.targetRuntime}.vsix`,
            inputDirectory: vsixDeployment.contents,
            useUriEncoding: true,
            fixUnixPermissions: qualifier.targetRuntime === "osx-x64"
        });

        return vsix;
    }

    /**
     * Builds a deployment definition representing everything that will be packaged in the VSIX for the DScript plugin
     * with the given version and server assembly.
     * Note: This function is relying on the checked-in compiled version extension.ts (extension.js) in order
     * to avoid doing TS compilations in-build (that has to deal with installing packages, etc.). But this
     * means that any change to client/src/extension.ts needs to be recompiled and the checked-in file updated.
     */
    export function buildVsixDeploymentDefinition(serverAssembly: ManagedSdk.Assembly) : Deployment.Definition {
        // Update the version of the manifest and json files to match the one being passed

        let manifest = IDE.VersionUtilities.updateVersion(Branding.semanticVersion, f`pluginTemplate/extension.vsixmanifest`);
        let json = IDE.VersionUtilities.updateVersion(Branding.version, f`client/package.json`);

        const vsixDeployment: Deployment.Definition = {
            contents: [
                {
                    subfolder: a`extension`,
                    contents: [
                        {
                            subfolder: a`bin`,
                            contents: [
                                serverAssembly,
                            ]
                        },
                        {
                            subfolder: a`resources`,
                            contents: [
                                ...globR(d`client/resources`),
                                Branding.iconFile,
                            ]
                        },
                        {
                            subfolder: a`snippets`,
                            contents: globR(d`client/snippets`)
                        },
                        {
                            subfolder: a`syntaxes`,
                            contents: globR(d`client/syntaxes`)
                        },
                        {
                            subfolder: a`themes`,
                            contents: globR(d`client/themes`)
                        },
                        {
                            subfolder: a`projectManagement`,
                            contents: globR(d`client/projectManagement`)
                        },
                        f`client/License.txt`,
                        f`client/package.nls.json`,
                        f`client/README.md`,
                        f`client/ThirdPartyNotices.txt`,
                        Branding.pngFile,
                        json,

                        // This contains the actual extension source as well as the
                        // node_modules that it depends on.
                        Transformer.sealDirectory({
                            root: d`pluginTemplate/extension`, 
                            files: globR(d`pluginTemplate/extension`)
                        }),
                    ]
                },
                f`pluginTemplate/[Content_Types].xml`,
                manifest
            ]
        };

        return vsixDeployment;
    }
}
