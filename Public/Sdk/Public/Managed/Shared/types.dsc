// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";

/**
 * Values allowed to be passed as references
 */
@@public
export type Reference = Binary | Assembly | ManagedNugetPackage;

/**
 * Represents a loose managed binary on disk without any semantics
 */
@@public
export interface Binary extends Deployment.Deployable {
    /**
     * The dll or exe file
     */
    binary: File;

    /**
     * The file containing the sybmols
     */
    pdb?: File;

    /**
     * The optional documentation
     */
    documentation?: File;
}

@@public
export interface Assembly extends Deployment.Deployable {
    /**
     * The name of the assembly
     */
    name: PathAtom,

    /**
     * The target framework this assembly as compiled against
     */
    targetFramework: string,

    /**
     * The assembly to pass for compilation
     */
    compile?: Binary;

    /**
     * The assembly to use at runtime
     */
    runtime?: Binary;

    /**
     * Optional native executable (for programs compiled to native with ILCompiler)
     */
    nativeExecutable?: File;

    /**
     * The direct references of the assembly.
     */
    references: Reference[];

    /**
     * The runtime configuration file. App.config for Desktop Clr or the runtime.json files for DotNet Core
     */
    runtimeConfigFiles?: File[];

    /**
     * If the runtime needs extra files to be deployed
     */
    runtimeContent?: Deployment.Definition;

    /**
     * List of deployable items to skip when deploying the dependencies of this assembly.
     * This is usefull for when you take a dependency on an assembly or a package but it comes with files or nuget packages
     * that conflict with other dependencies.
     */
    runtimeContentToSkip?: Deployment.DeployableItem[];
}

/**
 * A managed nuget package
 */
@@public
export interface ManagedNugetPackage extends NugetPackage, Deployment.Deployable {
    /**
     * The name of the nuget package
     */
    name: string;

    /**
     * The verison of the nuget package
     */
    version: string;

    /**
     * The assemblies to be used by compilation
     */
    compile: Binary[];

    /**
     * The assemblies needed at runtime
     */
    runtime: Binary[];

    /**
     * Roslyn analyzers that can validate the referencing project
     */
    analyzers?: Binary[];

    /**
     * Extra content/files to be deployed with the assembly when running. i.e. native dlls that are PIvoked, config files etc.
     */
    runtimeContent?: Deployment.Definition;
}

@@public
export interface LinkResource {
    /** Resource file to link. */
    file: File;

    /** Indicates whether the link resources is public or private. */
    isPublic?: boolean;

    /** Name of the resource. */
    logicalName?: string;
}

@@public
export function isBinary(item: Reference) : item is Binary {
    return item["binary"] !== undefined;
}

@@public
export function isAssembly(item: Reference) : item is Assembly {
    return item["name"] !== undefined &&
           (item["compile"] !== undefined || item["runtime"] !== undefined) &&
           item["contents"] === undefined; // Exclude nuget packages
}

@@public
export function isManagedPackage(item: any) : item is ManagedNugetPackage {
    return item["compile"] !== undefined ||
           item["runtime"] !== undefined ||
           item["runtimeContent"] !== undefined ||
           item["analyzers"] !== undefined;
}

@@public
export function getExecutable(assembly: Assembly): File {
    return assembly.nativeExecutable || assembly.runtime.binary;
}
