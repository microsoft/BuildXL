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
     * Layout containing the core bxl app.
     * For now this is just the netcore3.1 win-x64 version of bxl.
     * Internal only.
     **/
     @@public
    export const deployed = BuildXLSdk.Flags.isMicrosoftInternal && Context.getCurrentHost().os === "win"
        ? BuildXLSdk.DeploymentHelpers.deploy({
            definition: getPackageDeployment().deployedDefinition,
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

        // Same for .npmrc
        const npmrc = Transformer.copyFile(
            f`.npmrc`, 
            p`${targetDir}/.npmrc`, 
            /*tags*/[], 
            /*description*/ undefined, 
            /*keepOutputsWritable*/ true);

        const versionedJson = JS.Npm.version({
            packageJson: packageJson,
            nodeTool: InternalJS.Node.tool,
            npmTool: InternalJS.Node.getNpmTool(),
            version: Branding.version,
            additionalDependencies: [npmrc]
        });

        // Put the rest of the deployment side-by-side
        const definition : Deployment.Definition = {
            contents: [
                versionedJson,
                npmrc,
                f`tsconfig.json`,
                {
                    subfolder: r`build/win-x64`,
                    contents: [
                                importFrom("BuildXL.App").withQualifier({
                                    targetFramework: "netcoreapp3.1",
                                    targetRuntime: "win-x64",
                                    configuration: qualifier.configuration}).minimalDeployment,
                              ]
                },
            ]
        };
    
        return deploy(
            definition, 
            targetDir,
            // Let's try to strip down non-essential files
            {skipPdb: true, skipXml: true},
            // we might need to update the produced layout, so let's not seal it
            true,
            // tar is smart enough to honor hardlinks, but that causes problems when installing. So let's
            // keep outputs writable so we avoid hardlinks
            true );
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