// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * Options for graph introspection functions.
 */
@@public
export interface GraphOptions {
    /** When true, only return inputs that are outputs from other pips (excludes source files and source directories). */
    excludeSources?: boolean;
}

/**
 * Given the result of executing a process pip, returns the direct statically declared input artifacts (files and static directories) of it.
 * The order is deterministic for a given build graph.
 */
@@public
export function getDirectDependencies(pip: TransformerExecuteResult, options?: GraphOptions) : (File | StaticDirectory)[]
{
    return <(File | StaticDirectory)[]>_PreludeAmbientHack_Graph.getDirectDependencies(pip, options);
}

/**
 * Similar to 'getDirectDependencies' but returns the transitive closure of statically declared input artifacts for a given process pip.
 * The order is deterministic for a given build graph.
 */
@@public
export function getDependencyClosure(pip: TransformerExecuteResult, options?: GraphOptions) : (File | StaticDirectory)[]
{
    return <(File | StaticDirectory)[]>_PreludeAmbientHack_Graph.getDependencyClosure(pip, options);
}
