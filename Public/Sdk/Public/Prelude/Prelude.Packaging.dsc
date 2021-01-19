// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.Configuration.dsc"/>

/** Module description that could be used to configure a module */
interface ModuleDescription {
    /** Name of the package */
    name: string;
    /** Version of the package */
    version?: string;
    /** Package publisher */
    publisher?: string;
    /** Projects that are owned by this package. */
    projects?: (Path | File)[];
    /** Whether this package follows the legacy semantics or DScript V2 semantics for module resolution*/
    nameResolutionSemantics?: NameResolutionSemantics;
    /** Modules that are allowed as dependencies of this module. If this field is ommited, any module is allowed.*/
    allowedDependencies?: ModuleReference[];
    /** Dependent modules that are allowed to be part of a module-to-module dependency cycle. If ommited, 
     * cycles are not allowed. */
    cyclicalFriendModules?: ModuleReference[];


    // Deprecated V1 items
    /** Qualifier types relevant to the package. V1-specific. */
    @@obsolete("V1 modules are no longer supported. Please upgrade to V2 by setting the 'projects' field.")
    qualifierSpace?: QualifierSpace;

    /** Optional main entry point for the package. Default is 'package.dsc'. V1 specific. */
    @@obsolete("V1 modules are no longer supported. Please upgrade to V2 by setting the 'projects' field.")
    main?: File; 

    /**
     * The set of mounts defined by the module.
     * These mounts contribute to the global collection of mounts defined in the main configuration file
     */
    mounts?: Mount[];
}

/** A reference to a module. A string for now, but this might be expanded in the future. */
type ModuleReference = string;

/** Configuration function that is used in package.config.dsc for configuring a package. 
 * Obsolete. Use module() instead
*/
//@@obsolete
declare function package(package: ModuleDescription): void;

/** Configuration function that is used in package.config.dsc for configuring a package. */
declare function module(module: ModuleDescription): void;

// We are placing nuget packages here for now because else we have a bootstrap problem.
interface NugetPackage {
    /** The name of the nuget package */
    name?: string;

    /** The verison of the nuget package */
    version?: string;

    /** The contents of the nuget package */
    contents: StaticDirectory;

    /** The dependencies of this package */
    dependencies: NugetPackage[];
}

const enum NameResolutionSemantics {
    /** DScript V1 resolution semantics for a module: Explicit project and module imports, with a main file representing the module exports */
    explicitProjectReferences,
    /** DScript V2 resolution semantics for a module: Implicit project references but explicit module references with private/internal/public visibility on values */
    implicitProjectReferences
}
