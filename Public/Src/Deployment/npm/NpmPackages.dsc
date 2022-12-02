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
     * We currently deploy either Win or Linux package depending on the current host OS.
     * Internal only.
     **/
     @@public
    export const deployedWin = BuildXLSdk.Flags.isMicrosoftInternal && Context.getCurrentHost().os === "win"
        ? BuildXLSdk.DeploymentHelpers.deploy({
            definition: DeploymentRuntime.withQualifier(
                {   configuration: qualifier.configuration, 
                    targetRuntime: "win-x64"
                }).getPackageDeployment().deployedDefinition,
            targetLocation: r`${qualifier.configuration}/npm/win-x64`})
        : undefined;

        @@public
        export const deployedLinux = BuildXLSdk.Flags.isMicrosoftInternal && Context.getCurrentHost().os === "unix"
            ? BuildXLSdk.DeploymentHelpers.deploy({
                definition: DeploymentRuntime.withQualifier(
                    {   configuration: qualifier.configuration, 
                        targetRuntime: "linux-x64"
                    }).getPackageDeployment().deployedDefinition,
                targetLocation: r`${qualifier.configuration}/npm/linux-x64`})
            : undefined;
    

    namespace DeploymentRuntime {
        export declare const qualifier : { 
            configuration: "debug" | "release",
            targetRuntime: "linux-x64" | "win-x64" };

        export function getPackageDeployment() : Deployment.OnDiskDeployment {
            const targetDir = Context.getNewOutputDirectory("npm-bxl-internal-deploy");

            // Copy the template for package json to the place where we'll build the deployment and version it 
            const packageJson = Transformer.copyFile(
                qualifier.targetRuntime === "win-x64" ? f`package-win.json` : f`package-linux.json`, 
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

            const versionedJson = InternalJS.Node.runNpmVersion(
                packageJson,
                Branding.version,
                [npmrc]
            );

            // Put the rest of the deployment side-by-side
            const definition : Deployment.Definition = {
                contents: [
                    versionedJson,
                    npmrc,
                    f`tsconfig.json`,
                    {
                        subfolder: r`build/${qualifier.targetRuntime}`,
                        contents: [
                                    importFrom("BuildXL.App").withQualifier({
                                        targetFramework: Managed.TargetFrameworks.DefaultTargetFramework,
                                        targetRuntime: qualifier.targetRuntime,
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
}