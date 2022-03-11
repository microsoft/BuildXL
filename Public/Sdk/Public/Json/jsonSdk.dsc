// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

@@public
/** Writes an object as Json */
export function write<T extends Object>(destinationFile: Path, data: T, quoteChar?: "'" | "\"", tags?: string[], description?: string, additionalOptions? : AdditionalJsonOptions): File
{
    return _PreludeAmbientHack_Json.write<T>(destinationFile, data, quoteChar, tags, description, additionalOptions);
}

/** Additional options that can be specified for Json output. */
@@public
export interface AdditionalJsonOptions
{
    // Optionally indicate whether paths should have any special escaping added before being written.
    pathRenderingOption? : PathRenderingOption;
}

/**
 * Indicate how Paths should be rendered when written to Json.
 * - none: No additional transformations are performed, path separator will be based on OS (default).
 * - backSlashes: Always use backs lashes as path separator (not escaped).
 * - escapedBackSlashes: Always use back slashes as path separator with escape characters.
 * - forwardSlashes: Always use forward slashes as path separator.
 * 
 * CODESYNC: Public/Sdk/Public/Transformers/Transformer.Write.dsc
 */
@@public
export type PathRenderingOption = "none" | "backSlashes" | "escapedBackSlashes" | "forwardSlashes";