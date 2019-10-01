// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/** 
 * Interface that represents a deployment definition
 * This instance can be used to create a specific layout on disk, in a package, a drop etc. 
 */
@@public
export interface Definition {
    contents: DeployableItem[];
}

@@public
export interface DeploymentOptions {
    excludedDeployableItems?: DeployableItem[];
}

/**
 * Items that can be deployed
 */
@@public
export type DeployableItem = File | StaticDirectory | RenamedFile | Definition | NestedDefinition | Deployable;

/**
 * Indicates a renamed file in the deployment definiton
 */
@@public
export interface RenamedFile {
    file: File;
    targetFileName: PathAtom | string;
}

/**
 * A definition that can be nested inside another definition under a folder
 */
@@public
export interface NestedDefinition extends Definition {
    subfolder: PathFragment;
}

/**
 * Type that represents a deployed file and is disambiguation data
 */
@@public
export interface DeployedFileWithProvenance {
    file: File, 
    provenance?: Diagnostics.Provenance
}

/**
 * Helper type alias for the result of flattening 
 */
@@public
export interface FlattenedResult {
    flattenedFiles: Map<RelativePath, DeployedFileWithProvenance>,
    flattenedOpaques: Map<RelativePath, [OpaqueDirectory, RelativePath]>,  // Tuple of (1) an opaque directory, and (2) a path relative to that opaque directory designating a subdirectory to be added to the deployment
    visitedItems: Set<Object>,
}

/** 
 * Helper type alias for handeling duplicate file deployments. It can decide to return one or the other, but commonly it should just fail.
 * The canaonical implementation is Diagnostics.reportDuplicateError 
 */
@@public
export type HandleDuplicateFileDeployment =  (targetFile: RelativePath, sourceA: DeployedFileWithProvenance, sourceB: DeployedFileWithProvenance, message?: string) => DeployedFileAction;

@@public
export type DeployedFileAction = "takeA" | "takeB";

/**
 * Helper type for filtered deployed static directory
 */
@@public
export interface DeployableStaticDirectoryWithFolderFilter extends Deployable {
    staticDirectory: StaticDirectory;
    folderFilter: RelativePath;
}

/** 
 * Interface  for objects that are deployable 
 */
@@public
export interface Deployable {

    /** 
     * Callback for when deployments will be processed. By processing we mean flattening the recursive structure into a flat list which is encoded by the FlattenedResult type.
     * @param item - The item that is deployable. Think of this as the 'this' pointer which is not accessable from interface implementations.
     * @param targetFolder - The folder to place this deployable item into
     * @param onDuplicate - The error handler for duplicate files
     * @param currentResult - The current flattened result to add the extra flattened files to 
     * @return - The updated flattened result.
     */
    deploy: FlattenForDeploymentFunction;
}

@@public
export type FlattenForDeploymentFunction = (
        item: Object, 
        targetFolder: RelativePath,
        handleDuplicateFile: HandleDuplicateFileDeployment, 
        currentResult: FlattenedResult,
        deploymentOptions?: Object,
        provenance?: Diagnostics.Provenance) => FlattenedResult;
