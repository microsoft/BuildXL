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
};