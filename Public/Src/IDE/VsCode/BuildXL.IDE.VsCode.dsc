// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Transformer} from "Sdk.Transformers";
import * as ManagedSdk from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import * as Transformers from "Sdk.Transformers";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Branding from "BuildXL.Branding";
import * as VSIntegration from "BuildXL.Ide.VsIntegration";
import {Node, Npm} from "Sdk.NodeJs";

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
            fixUnixPermissions: qualifier.targetRuntime === "osx-x64",
            additionalDependencies: vsixDeployment.targetOpaques
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

        // We have to publish the vsix to the Visual Studio MarketPlace which doesn't handle prerelease tags. 
        let version = Branding.versionNumberForToolsThatDontSupportPreReleaseTag;
        let manifest = IDE.VersionUtilities.updateVersion(version, f`pluginTemplate/extension.vsixmanifest`);
        let json = IDE.VersionUtilities.updateVersion(version, f`client/package.json`);
        let readme = IDE.VersionUtilities.updateVersion(Branding.version, f`client/README.md`);

        const copyOfSourceFolder = copyDirectory(d`client`, Context.getNewOutputDirectory(`ClientTemp`));
        const nodeModulesPath = Npm.installFromPackageJson(copyOfSourceFolder).nodeModules;
        const outPath = Npm.runCompile(copyOfSourceFolder, nodeModulesPath);

        // Debug.writeLine("nodeModulesPath: " + nodeModulesPath.getContent().length);

        // TODO: package-lock.json to cg folder

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
                        {
                            subfolder: a`node_modules`,
                            contents: [ nodeModulesPath ]
                        },
                        {
                            subfolder: a`out`,
                            contents: [ outPath ]
                        },
                        f`client/License.txt`,
                        f`client/package.nls.json`,
                        readme,
                        f`client/ThirdPartyNotices.txt`,
                        Branding.pngFile,
                        json,
                    ]
                },
                f`pluginTemplate/[Content_Types].xml`,
                manifest,
            ]
        };

        return vsixDeployment;
    }

    function copyDirectory(fromDirectory : Directory, toDirectory : Directory): StaticDirectory {
        const onDiskDeployment = Deployment.deployToDisk({
            definition: Deployment.createFromDisk(fromDirectory),
            targetDirectory: toDirectory
        });
        return onDiskDeployment.contents;
        // Debug.writeLine("Deployment: " + Deployment.createFromDisk(fromDirectory));
        // Debug.writeLine(`=== ${onDiskDeployment.contents.root}: ${onDiskDeployment.contents.getContent().length}`);
    }
}
