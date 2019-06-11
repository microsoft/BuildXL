// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

import * as Deployment from "Sdk.Deployment";
import * as Frameworks from "Sdk.Managed.Frameworks";
import * as Shared from "Sdk.Managed.Shared";
import * as MacOS from "Sdk.MacOS";

export function deployAssemblyToDisk(assembly: Shared.Assembly, targetDirectory?: Directory, primaryFile?: RelativePath) : Deployment.OnDiskDeployment {
    primaryFile = primaryFile || r`${Shared.getExecutable(assembly).name}`;

    if (!targetDirectory) {
        targetDirectory = Context.getNewOutputDirectory(primaryFile.name.concat("-deployment"));
    }

    const definition : Deployment.Definition = {
        contents: [
            assembly
        ]
    };

    return Deployment.deployToDisk({
        definition: definition,
        targetDirectory: targetDirectory,
        primaryFile: primaryFile,
    });
}

/**
 * Deploys a managed executable assembly as a tool.
 */
@@public
export function deployManagedTool(args: DeployWithToolDefinitionArguments) : Transformer.ToolDefinition {
    let primaryFile = r`${Shared.getExecutable(args.tool).name}`;
    if ([ a`.exe`, a`.dll` ].some(ext => ext === primaryFile.extension)) {
        primaryFile = primaryFile.changeExtension(qualifier.targetRuntime === "win-x64" ? ".exe" : "");
    }
    const onDiskDeployment = deployAssemblyToDisk(args.tool, args.targetDirectory, primaryFile);

    let deploymentDefinition : Transformer.ToolDefinition = {
        exe: onDiskDeployment.primaryFile,
        description: args.description,
        dependsOnCurrentHostOSDirectories: true,
        // Opting to take individual file dependencies rather than as a sealed directory dependency
        runtimeDependencies: [
            ...onDiskDeployment.contents.getContent(),
        ]
    };

    if (args.options)
    {
        deploymentDefinition = args.options.merge<Transformer.ToolDefinition>(deploymentDefinition);
    }

    return Shared.Factory.createTool(deploymentDefinition);
}

@@public
export interface DeployWithToolDefinitionArguments {
    /**
     * The assembly contain
     */
    tool: Shared.Assembly,

    /**
     * Optional directory where to deploy this assembly to.
     * A new output directory will be created based on the assembly name.
     */
    targetDirectory?: Directory,

    /**
     * Common description that will be used for all pips.
     **/
    description?: string;

    /**
     * Tool Definition options
     */
    options?: Transformer.ToolDefinitionOptions;
}
