// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * Contains shortcuts and abstractions that make easy for users to their workflow
 * without being aware of DScript details like the Context object.
 */

/** True if the current OS is Windows. */
@@public
export const isWindows = OS.isWindows;

/** True if the current OS is Linux. */
@@public
export const isLinux = OS.isLinux;

/**
 * Gets mount path.
 *
 * Typical use of using Context.getMount is to get the mount path. Moreover, to lower
 * the cognitive load of users, the function hides the Context object.
 *
 * @param name Mount name.
 * @returns Mount path.
 */
@@public
export function getMount(name: string) : Path { return Context.getMount(name).path; }

/**
 * Gets string representation of a path.
 *
 * This function hides the uglieness of having to use Debug.dumpData.
 * 
 * @param p Path to be converted to string.
 * @returns String representation of a path.
 */
@@public
export function str(p: Path | Directory | File | RelativePath) : string { return Debug.dumpData(p); }

/**
 * Gets a new output directory.
 *
 * This function hides the existence of the Context object.
 *
 * @param hint Hint for the name of the output directory.
 */
@@public
export function getNewOutputDirectory(hint: string) : Directory { return Context.getNewOutputDirectory(hint); }