// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DeploymentSdk from "Sdk.Deployment";

namespace Deployment {

    // The deployment functionality is not qualifier specific
    export declare const qualifier : {};

    @@public
    export interface FlattenOptions extends DeploymentSdk.DeploymentOptions {
        skipDll?: boolean;
        skipPdb?: boolean;
        skipXml?: boolean;
        skipRuntimeConfigFiles?: boolean;
        skipReferences?: boolean;
        skipRuntimeContent?: boolean;
        skipNuGetAssets?: boolean;
    }
    
    const emptyOptions : FlattenOptions = {};

    /**
     * Implementation of IDeployment.deploy function for Managed Binary objects
     */
    @@public
    export function flattenBinary (
        binary: Binary,
        targetFolder: RelativePath,
        handleDuplicate: DeploymentSdk.HandleDuplicateFileDeployment,
        currentResult: DeploymentSdk.FlattenedResult,
        deploymentOptions: FlattenOptions,
        provenance: DeploymentSdk.Diagnostics.Provenance): DeploymentSdk.FlattenedResult {

        let result = currentResult;
        if (result.visitedItems.contains(binary))
        {
            return result;
        }
        else
        {
            result = {
                flattenedFiles: result.flattenedFiles,
                flattenedOpaques: result.flattenedOpaques,
                visitedItems: result.visitedItems.add(binary),
            };
        }

        deploymentOptions = deploymentOptions || emptyOptions;
        provenance = provenance && DeploymentSdk.Diagnostics.chain(provenance, `Managed Binary: '${binary.binary.name}'`);

        if (!deploymentOptions.skipDll && binary.binary) {
            result = addAssemblyFile(binary.binary, targetFolder, handleDuplicate, result, deploymentOptions, provenance);
        }

        if (!deploymentOptions.skipPdb && binary.pdb) {
            result = addAssemblyFile(binary.pdb, targetFolder, handleDuplicate, result, deploymentOptions, provenance);
        }

        if (!deploymentOptions.skipXml && binary.documentation) {
            result = addAssemblyFile(binary.documentation, targetFolder, handleDuplicate, result, deploymentOptions, provenance);
        }

        return result;
    }

    /**
     * Implementation of IDeployment.deploy function for Managed Assembly objects
     */
    @@public
    export function flattenAssembly(
        assembly: Assembly,
        targetFolder: RelativePath,
        handleDuplicate: DeploymentSdk.HandleDuplicateFileDeployment,
        currentResult: DeploymentSdk.FlattenedResult,
        deploymentOptions: FlattenOptions,
        provenance: DeploymentSdk.Diagnostics.Provenance): DeploymentSdk.FlattenedResult {
    
        let result = currentResult;
        if (result.visitedItems.contains(assembly))
        {
            return result;
        }
        else
        {
            result = {
                flattenedFiles: result.flattenedFiles,
                flattenedOpaques: result.flattenedOpaques,
                visitedItems: result.visitedItems.add(assembly),
            };
        }

        deploymentOptions = deploymentOptions || emptyOptions;

        if (assembly.runtimeContentToSkip) {
            // Avoid deploying content to skip by marking them as 'visited' already.
            result = {
                flattenedFiles: result.flattenedFiles,
                flattenedOpaques: result.flattenedOpaques,
                visitedItems: result.visitedItems.add(...assembly.runtimeContentToSkip),
            };
        }

        let runtimeProvenance = chainAssemblyProvenance(provenance, assembly, 'Runtime files');

        // TODO: optimize deployment for assemblies with nativeExecutable (they don't need referenced managed assemblies etc.)
        if (assembly.nativeExecutable)
        {
            result = addAssemblyFile(assembly.nativeExecutable, targetFolder, handleDuplicate, result, deploymentOptions, runtimeProvenance);
        }

        if (assembly.runtime && assembly.runtime.binary !== assembly.nativeExecutable)
        {
            result = flattenBinary(assembly.runtime, targetFolder, handleDuplicate, result, deploymentOptions, runtimeProvenance);
        }

        if (assembly.runtimeConfigFiles && (!deploymentOptions.skipRuntimeConfigFiles || !deploymentOptions.skipRuntimeContent)) {
            for (let runtimeConfigFile of assembly.runtimeConfigFiles) {
                result = addAssemblyFile(runtimeConfigFile, targetFolder, handleDuplicate, result, deploymentOptions, runtimeProvenance);
            }
        }

        let contentProvenance = chainAssemblyProvenance(provenance, assembly, 'Runtime contents');
        if (!deploymentOptions.skipRuntimeContent && assembly.runtimeContent) {
            result = DeploymentSdk.flattenRecursive(assembly.runtimeContent, targetFolder, handleDuplicate, result, deploymentOptions, contentProvenance);
        }

        let referenceProvenance = chainAssemblyProvenance(provenance, assembly, 'References');
        if (!deploymentOptions.skipReferences && assembly.references) {
            for (let reference of assembly.references) {
                if (!result.visitedItems.contains(reference))
                {
                    result = flattenDeployable(reference, targetFolder, handleDuplicate, result, deploymentOptions, referenceProvenance);
                }
            }
        }

        return result;
    }

    @@public
    export function flattenPackage(
        pkg: ManagedNugetPackage,
        targetFolder: RelativePath,
        handleDuplicate: DeploymentSdk.HandleDuplicateFileDeployment,
        currentResult: DeploymentSdk.FlattenedResult,
        deploymentOptions: FlattenOptions,
        provenance: DeploymentSdk.Diagnostics.Provenance): DeploymentSdk.FlattenedResult {

        let result = currentResult;

        if (result.visitedItems.contains(pkg))
        {
            return result;
        }
        else
        {
            result = {
                flattenedFiles: result.flattenedFiles,
                flattenedOpaques: result.flattenedOpaques,
                visitedItems: result.visitedItems.add(pkg),
            };
        }

        deploymentOptions = deploymentOptions || emptyOptions;
        

        // Add the runtime binaries
        let runtimeProvenance = chainNugetProvenance(provenance, pkg, 'Runtime assemblies');
        for (let runtime of pkg.runtime)
        {
            result = flattenDeployable(runtime, targetFolder, handleDuplicate, result, deploymentOptions, runtimeProvenance);
        }

        // Add the contents
        let contentProvenance = chainNugetProvenance(provenance, pkg, 'Content');
        if (pkg.runtimeContent)
        {
            result = DeploymentSdk.flattenRecursive(pkg.runtimeContent, targetFolder, handleDuplicate, result, deploymentOptions, contentProvenance);
        }

        // Walk the nuget dependencies
        let dependencyProvenance = chainNugetProvenance(provenance, pkg, 'Dependencies');
        for (let dependency of pkg.dependencies)
        {
            if (isManagedPackage(dependency))
            {
                result = flattenDeployable(dependency, targetFolder, handleDuplicate, result, deploymentOptions, dependencyProvenance);
            }
        }

        return result;
    }

    function flattenDeployable(
        deployable: DeploymentSdk.Deployable,
        targetFolder: RelativePath,
        handleDuplicate: DeploymentSdk.HandleDuplicateFileDeployment,
        currentResult: DeploymentSdk.FlattenedResult,
        deploymentOptions: FlattenOptions,
        provenance: DeploymentSdk.Diagnostics.Provenance) {
        return deployable.deploy(deployable, targetFolder, handleDuplicate, currentResult, deploymentOptions, provenance);
    }

    function addAssemblyFile(
        file: File, 
        targetFolder: RelativePath, 
        handleDuplicate: DeploymentSdk.HandleDuplicateFileDeployment, 
        currentResult: DeploymentSdk.FlattenedResult, 
        deploymentOptions: FlattenOptions, 
        provenance: DeploymentSdk.Diagnostics.Provenance) : DeploymentSdk.FlattenedResult {

        return DeploymentSdk.flattenFile(file, r`${targetFolder}/${file.name}`, handleDuplicate, currentResult, provenance);
    }

    export function chainAssemblyProvenance(provenance: DeploymentSdk.Diagnostics.Provenance, assembly: Assembly, marker: string) : DeploymentSdk.Diagnostics.Provenance {
        return provenance && DeploymentSdk.Diagnostics.chain(provenance, `Assembly: '${assembly.name}', tfm: '${assembly.targetFramework}' (${marker})`);
    }

    export function chainNugetProvenance(provenance: DeploymentSdk.Diagnostics.Provenance, pkg: ManagedNugetPackage, marker: string) : DeploymentSdk.Diagnostics.Provenance {
        return provenance && DeploymentSdk.Diagnostics.chain(provenance, `Nuget package: '${pkg.name}', version: '${pkg.version}' (${marker})`);
    }

}
