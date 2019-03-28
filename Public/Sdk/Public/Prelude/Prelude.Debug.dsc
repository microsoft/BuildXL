// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Transformer.Arguments.dsc"/>

/** An intrinsic object that provides debugging support for build specs. */
namespace Debug {
    /** (Side Effect) Prints out (to stdout) each given object on its own line. */
    export declare function writeLine(...strings: any[]);

    /** Returns string representation of given command-line arguments. */
    export declare function dumpArgs(args: Argument[]): string;

    /** Dumps the current callstack with the given message. */
    export declare function dumpCallStack(message: string) : void;

    /** Dumps data into a string. */
    export declare function dumpData(data: any): string;

    /** Expands strings enclosed in curlies as absolute paths. */
    export declare function expandPaths(str: string): string;
    
    /** Launches the debugger. */
    export declare function launch(): void;
};
