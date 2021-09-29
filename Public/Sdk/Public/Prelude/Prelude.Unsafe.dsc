// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>
/// <reference path="Prelude.IO.dsc"/>

namespace Unsafe {
    /** Unsafe function to create output file. */
    export declare function outputFile(path: Path, rewriteCount?: number): File;

    /** Unsafe function to create an exclusive output directory. */
    export declare function exOutputDirectory(path: Path): StaticDirectory;
}
