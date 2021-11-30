import * as Branding from "BuildXL.Branding";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as JS from "Sdk.JavaScript";
import * as InternalJS from "Sdk.NodeJs";
import * as Managed from "Sdk.Managed";

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

namespace npmPackages {
    export declare const qualifier : { configuration: "debug" | "release" };
    
    /** 
     * Npm tarball file containing the core bxl app.
     * For now this is just the netcore3.1 win-x64 version of bxl.
     * Internal only.
     **/
     @@public
    export const deployed = BuildXLSdk.Flags.isMicrosoftInternal && Context.getCurrentHost().os === "win"
        ? BuildXLSdk.DeploymentHelpers.deploy({
            definition: {contents:[createBxlInternalNpmPackage()]},
            targetLocation: r`${qualifier.configuration}/npm`})
        : undefined;

    function getPackageDeployment() : Deployment.OnDiskDeployment {
        const targetDir = Context.getNewOutputDirectory("npm-bxl-internal-deploy");

        // Copy the template for package json to the place where we'll build the deployment and version it 
        const packageJson = Transformer.copyFile(
            f`package.json`, 
            p`${targetDir}/package.json`, 
            /*tags*/[], 
            /*description*/ undefined, 
            /*keepOutputsWritable*/ true);

        const versionedJson = JS.Npm.version({
            packageJson: packageJson,
            nodeTool: InternalJS.Node.tool,
            npmTool: InternalJS.Node.getNpmTool(),
            version: Branding.version
        });

        // Put the rest of the deployment side-by-side
        const definition : Deployment.Definition = {
            contents: [
                versionedJson,
                f`tsconfig.json`,
                {
                    subfolder: r`build/win-x64`,
                    contents: [
                                importFrom("BuildXL.App").withQualifier({
                                    targetFramework: "netcoreapp3.1",
                                    targetRuntime: "win-x64",
                                    configuration: qualifier.configuration}).deployment,
                              ]
                },
            ]
        };
    
        return deploy(
            definition, 
            targetDir,
            // Let's try to strip down non-essential files
            {skipPdb: true, skipXml: true},
            // npm pack places the tarball file in the same working directory, so let's not seal it
            true,
            // tar is smart enough to honor hardlinks, but that causes problems when installing. So let's
            // keep outputs writable so we avoid hardlinks
            true );
    }

    function createBxlInternalNpmPackage() : File {
        // Get the package final deployment
        const deployment = getPackageDeployment();

        // Run npm install to compile any potential code there
        const installedPackage = InternalJS.Node.runNpmInstall(
            deployment.contents.root, 
            [deployment.contents, ...deployment.targetOpaques]);

        // Run npm pack to create a tarball file
        const tarball = JS.Npm.pack({
            targetDirectory: deployment.contents.root, 
            nodeTool: InternalJS.Node.tool, 
            npmTool: InternalJS.Node.getNpmTool(),
            additionalDependencies: [deployment.contents, ...deployment.targetOpaques, installedPackage]
        });

        // CODESYNC: the package name is reflected in the tarball name. This needs to be in sync with 
        // Public\Src\Deployment\npm\package.json
        const tarballName : PathAtom = a`microsoft-buildxl-internal-${Branding.version}.tgz`; 

        return tarball.assertExistence(p`${deployment.contents.root}/${tarballName}`);
    }

    function deploy(
            definition: Deployment.Definition,
            targetLocation: Directory | PathFragment, 
            deploymentOptions?: Managed.Deployment.FlattenOptions, 
            keepOutputsWritable?: boolean,
            partiallySealDirectory?: boolean) : Deployment.OnDiskDeployment {
        
        deploymentOptions = Object.merge<Deployment.DeploymentOptions>(
            {
                skipXml: true,
            },
            deploymentOptions);

        const targetDirectory = typeof (targetLocation) === 'Directory' 
            ? <Directory> targetLocation 
            : d`${Context.getMount("DeploymentRoot").path}/${targetLocation}`; 

        return Deployment.deployToDisk({
            definition: definition,
            deploymentOptions: deploymentOptions,
            targetDirectory: targetDirectory, 
            tags: [
                'deployment',
                ...(deploymentOptions ? (deploymentOptions.tags || []) : [])
            ],
            sealPartialWithoutScrubbing: partiallySealDirectory,
            keepOutputsWritable: keepOutputsWritable
        });
    }
}