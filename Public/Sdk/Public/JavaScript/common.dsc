// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/**
 * Turns on some relaxations for executing tools
 */
export const defaults: Transformer.ExecuteArguments = {
    arguments: undefined,
    workingDirectory: undefined,
    allowUndeclaredSourceReads: true,
    // Many JS tools are case sensitive, so let's try to preserve case sensitivity on cache replay
    preservePathSetCasing: true,
    sourceRewritePolicy: "safeSourceRewritesAreAllowed",
    doubleWritePolicy: "allowSameContentDoubleWrites",
    // In some cases node.exe lingers around for some time, but should be safe to kill on teardown
    allowedSurvivingChildProcessNames: [a`node.exe`],
    // Default is to retry 3 times (for the cases where retry codes are specified, which is decided per tool)
    processRetries: 3,
};

/**
 * All install operations use this list of passthrough env vars
 */
export const defaultPassthroughVariables : string[] = [
    "APPDATA",
    "LOCALAPPDATA",
    "USERNAME",
    "HOMEDRIVE",
    "HOMEPATH",
    "INTERNETCACHE",
    "INTERNETHISTORY",
    "INETCOOKIES",
    "LOCALLOW"];

/**
 * Creates a tool definition from a static directory containing a tool 'installation'
 */
export function getTool(
    installation: StaticDirectory, 
    getDefaultInstallation: () => StaticDirectory, 
    toolName: () => PathAtom,
    relativePathToInstallation?: RelativePath) : Transformer.ToolDefinition {
    
    installation = installation || getDefaultInstallation();

    let toolFile = undefined;
    let tool = toolName();

    let isOpaque = installation.kind === "shared" || installation.kind === "exclusive";

    // If the specific location of the tool is not provided, try to find it under the static directory
    if (!relativePathToInstallation) {
        
        if (isOpaque) {
            Contract.fail(`An output directory must provide a relative path to locate the tool under the provided installation`);
        }

        let toolFound = installation.contents.find((file, index, array) => array[index].name === tool);
        if (toolFound !== undefined) {
            toolFile = toolFound;
        }
        else {
            Contract.fail(`Could not find ${tool} under the provided installation.`);
        }
    }
    else {
        // Otherwise, just get it from the static directory as specified
        toolFile = isOpaque
            ? (<OpaqueDirectory>installation).assertExistence(relativePathToInstallation) 
            : installation.getFile(relativePathToInstallation);
    }
    
    return {
        exe: toolFile,
        dependsOnWindowsDirectories: true,
        dependsOnCurrentHostOSDirectories: true,
        dependsOnAppDataDirectory: true,
        runtimeDirectoryDependencies: [installation],
        prepareTempDirectory: true};
}

/**
 * Resolves the location of .npmrc (or its global counterpart)
 */
export function resolveNpmrc(location: "local" | "userprofile" | File, targetFolder: Directory, npmrcName: PathAtom) : File {

    location = location || "userprofile";
    let npmrc = undefined;

    switch (location) {
        case "local": {
            npmrc = f`${targetFolder}/${npmrcName}`;
            break;
        }
        case "userprofile" : {
            npmrc = f`${Environment.getDirectoryValue("USERPROFILE")}/${npmrcName}`;
            break;
        }
        default: {
            npmrc = <File> location;
            break;
        }
    }
    
    return npmrc;
}

/**
 * Base interface for all install arguments
 */
export interface InstallArgumentsCommon {
    nodeTool: Transformer.ToolDefinition,
    additionalDependencies?: (File | StaticDirectory)[],
    environment?: Transformer.EnvironmentVariable[],
    processRetries?: number,
}


/**
 * The location of an .npmrc file.
 * userprofile -> the default behavior for npm, yarn, etc. where the .npmrc file is loaded from the user profile directory
 * local -> file loaded from a per-pip unique location, to prevent 'leaks' from any .npmrc file in the user profile directory
 * File -> custom-specified location
 * The default is 'userprofile'
  */
@@public
export type NpmrcLocation = "local" | "userprofile" | File;