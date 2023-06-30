// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

/** 
 * Utilities for accessing functionalities in ambient environment.
 * TODO: Add these environment functionalities to Ambient environment.
 */
namespace Env
{
    /** Gets a string value from an environment variable, or returns a default string when there is no such variable. */
    @@public
    export function getString(name: string, defaultValue: string): string
    {
        return Environment.hasVariable(name)
            ? Environment.getStringValue(name)
            : defaultValue;
    }

    /** Gets a path value from an environment variable, or returns a default path when there is no such variable. */
    @@public
    export function getPath(name: string, defaultValue: Path): Path
    {
        return Environment.hasVariable(name)
            ? Environment.getPathValue(name)
            : defaultValue;
    }

    /** Gets a boolean value from an environment variable, or returns a default boolean when there is no such variable. */
    @@public
    export function getFlag(name: string, defaultValue: boolean): boolean
    {
        return Environment.hasVariable(name)
            ? Environment.getFlag(name)
            : defaultValue;
    }

    /** Removes all occurences of TMP, TEMP, or TMPDIR from environment variables. */
    export function removeTemp(environmentVariables?: Transformer.EnvironmentVariable[]) : Transformer.EnvironmentVariable[]
    {
        if (environmentVariables === undefined) return undefined;
        return environmentVariables.filter(e =>
            e !== undefined
            && e.name !== "TEMP"
            && e.name !== "TMP"
            && (!OS.isLinux || e.name !== "TMPDIR"));
    }
}