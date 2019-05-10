// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

@@public
export const emptyFlattenedResult : FlattenedResult = {
    flattenedFiles: Map.empty<RelativePath, { file: File, disambiguationData: any }>(),
    flattenedOpaques: Map.empty<RelativePath, OpaqueDirectory>(),
    visitedItems: Set.empty<Object>(),
};

/**
 * Flattens a deployment definition into a map of relative path to file to be stored at that relative path.
 * This is typically called by the specific deploy functions before doing the actualy deployment operations.
 * @param definition - The definition to flatten
 * @param handleDuplicateFile - Optional handler for how to handle duplicate files. The default will be to report an error.
 * @param deploymentOptions - Optional options that functions that deploy can access. An example is for managed assemblies to control if they want to deploy the pdb and xml doc or not.
 */
@@public
export function flatten(
    definition: Definition,
    handleDuplicateFile?: HandleDuplicateFileDeployment,
    deploymentOptions?: DeploymentOptions): FlattenedResult {

    let initialFlattenResult = emptyFlattenedResult;
    if (deploymentOptions && deploymentOptions.excludedDeployableItems)
    {
        initialFlattenResult = initialFlattenResult.merge<FlattenedResult>({
            visitedItems: getInitialVisitedItems(deploymentOptions)
        });
    }

    return flattenItem(
        definition,
        r`.`,
        handleDuplicateFile || Diagnostics.reportDuplicateFileError,
        initialFlattenResult,
        deploymentOptions,
        Diagnostics.initialProvenance
    );
}


/**
 * Helper to flattens a Definition while already inside flattening functions.
 */
@@public
export function flattenRecursive(
    definition: Definition,
    targetFolder: RelativePath,
    handleDuplicateFile: HandleDuplicateFileDeployment,
    currentResult: FlattenedResult,
    deploymentOptions: DeploymentOptions,
    provenance: Diagnostics.Provenance
    ): FlattenedResult {

    let result = currentResult;

    for (let item of definition.contents) {
        result = flattenItem(item, targetFolder, handleDuplicateFile, result, deploymentOptions, provenance);
    }

    return result;
}

/**
 * Does the same work as 'flatten' but only returns the files instead of a map
 */
@@public
export function getFiles(defn: Definition) : File[] {
    let result = flatten(defn);
    return result.flattenedFiles.values().map(pair => pair.file);
}

/**
 * Flattens and retrieves all relative paths
 */
@@public
export function extractRelativePaths(deployment: Definition): [RelativePath,File][] {
    return flatten(deployment).flattenedFiles.toArray().map(kvp => <[RelativePath, File]>[kvp[0], kvp[1].file]);
}

function getInitialVisitedItems(deploymentOptions: DeploymentOptions) : Set<Object> {
    if (deploymentOptions && deploymentOptions.excludedDeployableItems)
    {
        return Set.create(...deploymentOptions.excludedDeployableItems);
    }

    return Set.empty<Object>();
}


function flattenItem(
    item: DeployableItem,
    targetFolder: RelativePath,
    handleDuplicateFile: HandleDuplicateFileDeployment,
    currentResult: FlattenedResult,
    deploymentOptions: DeploymentOptions,
    provenance: Diagnostics.Provenance)
    : FlattenedResult {

    if (item === undefined) {
        return currentResult;
    }

    // VisitedItems is used to skip deploying items.
    // It is not worth tracking all objects as visited since they deduplicate
    // but it is good to not deploy when explicitly requested not to.
    if (currentResult.visitedItems.contains(item))
    {
        return currentResult;
    }

    const contentType = typeof item;

    if (isStaticDirectory(item)) {
        return flattenStaticDirectory(item, targetFolder, handleDuplicateFile, currentResult, provenance);
    }

    if (isFile(item)) {
        return flattenFile(item, targetFolder.combine(item.name), handleDuplicateFile, currentResult, provenance);
    }

    if (isDeployable(item)) {
        return item.deploy(item, targetFolder, handleDuplicateFile, currentResult, deploymentOptions, provenance);
    }

    if (isNestedDefinition(item)) {
        // When we recurse into a directory we have to start with an empty set of visited items
        let nestedResult = <FlattenedResult>{
            flattenedFiles: currentResult.flattenedFiles,
            flattenedOpaques: currentResult.flattenedOpaques,
            visitedItems: getInitialVisitedItems(deploymentOptions),
        };

        nestedResult = flattenRecursive({contents: item.contents}, targetFolder.combine(item.subfolder), handleDuplicateFile, nestedResult, deploymentOptions, provenance);

        // Do not include visited items from the nested definition in the final result
        return <FlattenedResult>{
            flattenedFiles: nestedResult.flattenedFiles,
            flattenedOpaques: nestedResult.flattenedOpaques,
            visitedItems: currentResult.visitedItems
        };
    }

    if (isRenamedFile(item)) {
        return flattenFile(item.file, targetFolder.combine(item.targetFileName), handleDuplicateFile, currentResult, provenance);
    }

    if (isDefinition(item)) {
        return flattenRecursive(item, targetFolder, handleDuplicateFile, currentResult, deploymentOptions, provenance);
    }

    Contract.fail(`Unexpected item encountered in deployment. Expected a File, StaticDirectory, RenameFile, NestedDefinition, Deployable, got ${item} with type ${typeof item}`);
}

@@public
export function flattenFile(file: File, targetFile: RelativePath, handleDuplicateFile: HandleDuplicateFileDeployment, currentResult: FlattenedResult, provenance: Diagnostics.Provenance) : FlattenedResult
{
    let result = currentResult;

    // This could be more preformant if we had a getOrAdd operation.
    const existingFileWithProvenace = result.flattenedFiles.get(targetFile);

    const currentFileWithProvenace : DeployedFileWithProvenance = {
        file: file,
        provenance: provenance
    };

    // TODO: Validate if the file is going to be under an existing OpaqueDirectory. To implement this we'll need IsWithin on RelativePath

    if (existingFileWithProvenace !== undefined) {
        if (existingFileWithProvenace.file !== file) {
            // If the two files are different, report the error.
            let action = handleDuplicateFile(targetFile, existingFileWithProvenace, currentFileWithProvenace);
            switch  (action) {
                case "takeA":
                    // Do nothing just skip, the left one is already added.
                    return result;
                case "takeB":
                    // we need to remove the already added one and then add the current one.
                    return {
                        flattenedFiles: result
                            .flattenedFiles
                            .remove(targetFile)
                            .add(targetFile, currentFileWithProvenace),
                        flattenedOpaques: result.flattenedOpaques,
                        visitedItems: result.visitedItems,
                    };
                default:
                    Contract.fail("Invalid handleDuplicateFile handler. It must return either 'takeA', 'takeB' or fail evaluation");
            }
        }

        return result;
    } else {
        return {
            flattenedFiles: result.flattenedFiles.add(targetFile, currentFileWithProvenace),
            flattenedOpaques:result.flattenedOpaques,
            visitedItems: result.visitedItems,
        };
    }
}

function flattenStaticDirectory(staticDirectory: StaticDirectory, targetFolder: RelativePath, handleDuplicateFile: HandleDuplicateFileDeployment, currentResult: FlattenedResult, provenance: Diagnostics.Provenance) : FlattenedResult
{
    let result = currentResult;

    if (currentResult.visitedItems.contains(staticDirectory))
    {
        return result;
    }

    switch (staticDirectory.kind) {
        case "full":
        case "partial":
            for (let file of staticDirectory.getContent()) {
                const targetFile = r`${targetFolder}/${staticDirectory.getRelative(file.path)}`;
                result = flattenFile(file, targetFile, handleDuplicateFile, result, provenance);
            }

            return {
                flattenedFiles: result.flattenedFiles,
                flattenedOpaques: result.flattenedOpaques,
                visitedItems: result.visitedItems.add(staticDirectory),
            };

        case "shared":
        case "exclusive":
            const existingOpaque = result.flattenedOpaques.get(targetFolder);

            // TODO: Improve error logging and disambiguation
            if (existingOpaque !== undefined) {
                if (existingOpaque !== staticDirectory) {
                    Contract.fail(`Duplicate opaque directory. Can't deploy both '{existingOpaque.root}' and '{staticDirectory.root}' to '{targetFolder}'`);
                }

                return result;
            }
            else {
                // TODO: Validate if there is a flattenedFile already under this OpaqueDirectory. To implement this we'll need IsWithin on RelativePath
                return {
                    flattenedFiles: result.flattenedFiles,
                    flattenedOpaques: result.flattenedOpaques.add(targetFolder, <OpaqueDirectory>staticDirectory),
                    visitedItems: result.visitedItems.add(staticDirectory),
                };
            }

        default:
            Contract.fail(`Static Directory kind '${staticDirectory.kind}' not yet supported to deploy`);
            return result;
    }
}

/**
 * Creates a deployment definition from the given staticDirectory
 * @param staticDirectory - The StaticDiretory whose files should be included in the resulting deployment definitions
 * @param folderFilter - Folder to filter on, if you want all the files in the staticDirectory, then simply add the entire StaticDirectory to the deployment
 */
@@public
export function createFromFilteredStaticDirectory(staticDirectory: StaticDirectory, folderFilter: RelativePath) : DeployableStaticDirectoryWithFolderFilter {
    return {
        staticDirectory: staticDirectory,
        folderFilter: folderFilter,
        deploy: deployFilteredStaticDirectory,
    };
}

function deployFilteredStaticDirectory(
    item: DeployableStaticDirectoryWithFolderFilter,
    targetFolder: RelativePath,
    handleDuplicateFile: HandleDuplicateFileDeployment,
    currentResult: FlattenedResult,
    deploymentOptions: Object,
    provenance: Diagnostics.Provenance) : FlattenedResult {

    const staticDirectory = item.staticDirectory;
    const folderFilter = item.folderFilter;

    let result = currentResult;

    for (let file of staticDirectory.getContent()) {
        const filteredRoot = staticDirectory.path.combine(folderFilter);
        if (file.path.isWithin(filteredRoot)) {
            const targetFile = targetFolder.combine(filteredRoot.getRelative(file.path));
            result = flattenFile(file, targetFile, handleDuplicateFile, result, provenance);
        }
    }

    return result;
}

// Private helper functions for type discrimination

function isRenamedFile(item: DeployableItem) : item is RenamedFile {
    return item["targetFileName"] !== undefined;
}

function isFile(item:DeployableItem) : item is File {
    return typeof item === "File";
}

function isStaticDirectory(item:DeployableItem) : item is StaticDirectory {
    const itemType = typeof item;
    switch (itemType) {
        case "FullStaticContentDirectory":
        case "PartialStaticContentDirectory":
        case "SourceAllDirectory":
        case "SourceTopDirectory":
        case "SharedOpaqueDirectory":
        case "ExclusiveOpaqueDirectory":
        case "StaticDirectory":
            return true;
        default:
            false;
    }
}

function isNestedDefinition(item:DeployableItem) : item is NestedDefinition {
    return item["subfolder"] !== undefined;
}

function isDefinition(item:DeployableItem) : item is Definition {
    return item["contents"] !== undefined;
}

function isDeployable(item:DeployableItem) : item is Deployable {
    return item["deploy"] !== undefined;
}
