import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";

import {DropDaemonRunner, DropRunner, DropCreateResult, FileInfo, DirectoryInfo, Result as DropOperationResult, DropOperationArguments} from "BuildXL.Tools.DropDaemon";

namespace DeploymentHelpers {

    export declare const qualifier: {};
    
    @@public
    export function deploy(args: {
        definition: Deployment.Definition,
        targetLocation: RelativePath,
        deploymentOptions?: Deployment.DeploymentOptions,
    }) : Deployment.OnDiskDeployment {

        const deploymentOptions = Object.merge<Deployment.DeploymentOptions>(
             
                <Managed.Deployment.FlattenOptions>{
                    skipXml: true,
                },
            
            args.deploymentOptions);

        const deployedToDisk = Deployment.deployToDisk({
            definition: args.definition,
            deploymentOptions: deploymentOptions,
            targetDirectory: d`${Context.getMount("DeploymentRoot").path}/${args.targetLocation}`, 
            tags: ['deployment']
        });

        if (Drop.enabled) {
            // Create a deployment that places the one in a subtree.
            const deploymentInSubFolder: Deployment.Definition = {
                contents: [
                    {
                        subfolder: args.targetLocation,
                        contents: [args.definition]
                    }
                ]
            };

            Drop.addDeploymentToDrop(
                Drop.runner, 
                Drop.createResult, 
                {}, 
                deploymentInSubFolder,
                deploymentOptions);
        }

        return deployedToDisk;
    }


    namespace Drop {
        /** Whether this build should upload to drop or not */
        export const enabled = Environment.hasVariable("BUILDXL_DROP_ENABLED") ? Environment.getBooleanValue("BUILDXL_DROP_ENABLED") : false;

        /** The runner that preforms the upload */
        export const runner = enabled ? DropDaemonRunner.withQualifier({configuration: "release", targetFramework: "net472", targetRuntime: "win-x64"}).cloudBuildRunner : undefined;

        /** The settings for this drop */
        const settings = {
            dropServiceConfigFile: Environment.getFileValue("BUILDXL_DROP_CONFIG")
        };

        /** The drop create result to use for all uploads */
        export const createResult = enabled ? runner.createDrop(settings) : undefined;

        /** 
         * Helper method to add deployments to droprunners. 
         * Ideally this lives in the drop SDK. There is a microsoft internal customer that consumes the drop sdk but not yet the deployment sdk, 
         * so we unfortunately can't do it clean and introduce the dependency just yet...
         **/
        export function addDeploymentToDrop(
            runner: DropRunner, 
            createResult: DropCreateResult, 
            args: DropOperationArguments, 
            deployment: Deployment.Definition, 
            deploymentOptions?: Deployment.DeploymentOptions) : void
        {
            const flattenedResult = Deployment.flatten(deployment, undefined, deploymentOptions);

            // Add all the files via a batch call.
            const filesToAdd = flattenedResult.flattenedFiles.forEach(kvp => <FileInfo>{dropPath: kvp[0], file: kvp[1].file });
            const filesResult = runner.addFilesToDrop(createResult, args, filesToAdd);

            // Add all Opaque directories via a batch call.
            const directoriesToAdd = flattenedResult.flattenedOpaques.forEach(kvp => <DirectoryInfo>{dropPath: kvp[0], directory: kvp[1]});
            const directoryResults = runner.addDirectoriesToDrop(createResult, args, directoriesToAdd);
        };
    }
}