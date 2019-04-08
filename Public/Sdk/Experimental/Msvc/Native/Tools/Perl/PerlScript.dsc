// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

import {GenericScript} from "Sdk.Native.Shared";

/**
 * The default interpreter for perl.exe
 */
export function defaultInterpreter(): Transformer.ToolDefinition {
    Contract.fail("The default location for perl.exe has not been specified");
    return undefined;
}

/**
 * Arguments to pass to the PerlScriptRunner
 */
@@public
export interface Arguments extends GenericScript.Arguments {
    /**
     * Ignore text before #!perl line
     */
    @@Tool.option("-x")
    ignoreTextAtStart?: boolean;
}

/** Evaluate the Perl Script runner */
@@public
export function evaluate(inputArgs: Arguments): File[] {
    let args = <GenericScript.Arguments>{
        arguments: [
            Cmd.flag("-x", inputArgs.ignoreTextAtStart),
            Cmd.argument(Artifact.input(inputArgs.script.exe))
        ],
        interpreter: defaultInterpreter(),
    }
    .merge<GenericScript.Arguments>(inputArgs);
    return GenericScript.evaluate(args);
}
