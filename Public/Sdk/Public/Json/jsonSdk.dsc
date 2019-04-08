// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

@@public
/** Writes an object as Json */
export function write<T extends Object>(destinationFile: Path, data: T, quoteChar?: "'" | "\"", tags?: string[], description?: string): File
{
    return _PreludeAmbientHack_Json.write<T>(destinationFile, data, quoteChar, tags, description);
}
