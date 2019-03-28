// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Transformer.dsc"/>
/// <reference path="Prelude.Transformer.Arguments.dsc"/>

/** An intrinsic object that provides debugging support for build specs. */
namespace Debug {
    export declare function writeLine(...strings: any[]);
    export declare function dumpArgs(args: Argument[]): string;
    export declare function dumpCallStack(message: string) : void;
    export declare function dumpData(data: any): string;
    export declare function expandPaths(str: string): string;
    export declare function launch(): void;
    export declare function sleep(millis: number): void;
}
