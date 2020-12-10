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

namespace VsCode.Client {
    // A new namespace with empty qualifier space to ensure the values inside are evaluated only once
    export declare const qualifier: {};

    const clientSealDir = Transformer.sealDirectory(d`client`, globR(d`client`));

    const clientCopy: OpaqueDirectory = Deployment.copyDirectory(clientSealDir.root, Context.getNewOutputDirectory("client-copy"), clientSealDir);

    @public
    export const npmInstall = Npm.npmInstall(clientCopy, []);

    @@public
    export const compileOutDir: OpaqueDirectory = Node.tscCompile(
        clientCopy.root, 
        [ clientCopy, npmInstall ]);
}

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
            deploymentOptions: <Deployment.DeploymentOptions>({
                skipXml: true, 
                skipPdb: true,
                // CODESYNC: \Public\Src\FrontEnd\MsBuild\BuildXL.FrontEnd.MsBuild.dsc
                // The vscode vsix does not need to have the vbcscompiler from the msbuild frontend since that is only used during build, 
                // not during graph construction. This saves a lot of Mb's to keep us under the size limmit of the marketplace
                excludedDeployableItems: [
                    importFrom("BuildXL.Tools").VBCSCompilerLogger.withQualifier({ targetFramework: "net472" }).dll,
                    importFrom("BuildXL.Tools").VBCSCompilerLogger.withQualifier({ targetFramework: "net5.0" }).dll,
                ]
            }),
        });

        // Zip and return
        const vsix = VSIntegration.CreateZipPackage.zip({
            outputFileName: `BuildXL.vscode.${qualifier.targetRuntime}.vsix`,
            inputDirectory: vsixDeployment.contents,
            useUriEncoding: true,
            fixUnixPermissions: [ "osx-x64", "linux-x64" ].indexOf(qualifier.targetRuntime) !== -1 ,
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

        const vsixDeployment: Deployment.Definition = {
            contents: [
                {
                    subfolder: a`extension`,
                    contents: [
                        {
                            subfolder: a`bin`,
                            contents: [
                                serverAssembly,
                                importFrom("BuildXL.App").evaluationOnlySdks
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
                            contents: [ Deployment.createDeployableOpaqueSubDirectory(VsCode.Client.npmInstall, r`node_modules`) ]
                        },
                        {
                            subfolder: a`out`,
                            contents: [ VsCode.Client.compileOutDir ]
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
}
