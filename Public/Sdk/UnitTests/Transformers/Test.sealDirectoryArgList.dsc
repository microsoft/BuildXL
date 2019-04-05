// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";
import {Transformer} from "Sdk.Transformers";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function sealDirectoryWithoutTags() {
        Transformer.sealDirectory(d`src`, [f`src/file1`, f`src/file2`]);
    }
    
    @@Testing.unitTest()
    export function sealDirectoryWithTags() {
        Transformer.sealDirectory(d`src`, [f`src/file1`, f`src/file2`], ["aTag", "zTag"]);
    }

    @@Testing.unitTest()
    export function sealDirectoryWithDescription() {
        Transformer.sealDirectory(d`src`, [f`src/file1`, f`src/file2`], undefined, "Custom Description");
    }
    
    @@Testing.unitTest()
    export function sealDirectoryWithDescriptionScrub() {
        Transformer.sealDirectory(d`src`, [f`src/file1`, f`src/file2`], undefined, undefined, true);
    }
    
    @@Testing.unitTest()
    export function sealPartialDirectoryWithoutTags() {
        Transformer.sealPartialDirectory(d`src`, [f`src/file1`, f`src/file2`]);
    }
    
    @@Testing.unitTest()
    export function sealPartialDirectoryWithTags() {
        Transformer.sealPartialDirectory(d`src`, [f`src/file1`, f`src/file2`], ["aTag", "zTag"]);
    }
    
    @@Testing.unitTest()
    export function sealPartialDirectoryWithDescription() {
        Transformer.sealPartialDirectory(d`src`, [f`src/file1`, f`src/file2`], undefined, "Custom Description");
    }

    @@Testing.unitTest()
    export function sealSourceDirectoryAllDirectoriesWithoutTags() {
        Transformer.sealSourceDirectory(d`src`, Transformer.SealSourceDirectoryOption.allDirectories);
    }
    
    @@Testing.unitTest()
    export function sealSealSourceDirectoryAllDirectoriesWithTags() {
        Transformer.sealSourceDirectory(d`src`, Transformer.SealSourceDirectoryOption.allDirectories, ["aTag", "zTag"]);
    }
    
    @@Testing.unitTest()
    export function sealSealSourceDirectoryAllDirectoriesWithDescription() {
        Transformer.sealSourceDirectory(d`src`, Transformer.SealSourceDirectoryOption.allDirectories, undefined, "Custom description");
    }

    @@Testing.unitTest()
    export function sealSourceDirectoryTopDirectoryOnlyDefaultWithoutTags() {
        Transformer.sealSourceDirectory(d`src`);
    }
    
    @@Testing.unitTest()
    export function sealSourceDirectoryTopDirectoryOnlyWithoutTags() {
        Transformer.sealSourceDirectory(d`src`, Transformer.SealSourceDirectoryOption.topDirectoryOnly);
    }
    
    @@Testing.unitTest()
    export function sealSealSourceDirectoryTopDirectoryOnlyWithTags() {
        Transformer.sealSourceDirectory(d`src`, Transformer.SealSourceDirectoryOption.topDirectoryOnly, ["aTag", "zTag"]);
    }
    
    @@Testing.unitTest()
    export function sealSealSourceDirectoryAllDirectoriesWithPatterns() {
        Transformer.sealSourceDirectory(d`src`, Transformer.SealSourceDirectoryOption.allDirectories, ["aTag", "zTag"], "Custom description", ["*.cs", ".txt"]);
    }
    
    @@Testing.unitTest()
    export function sealSealSourceDirectoryTopDirectoryOnlyWithPatterns() {
        Transformer.sealSourceDirectory(d`src`, Transformer.SealSourceDirectoryOption.topDirectoryOnly, ["aTag", "zTag"], "Custom description", ["*.cs", ".txt"]);
    }

    @@Testing.unitTest()
    export function inspectingSealDirectoryKindIsAllowed() {
        // Any type of static directory will do, we are not checking the kind matches the operation, which is already
        // checked in a unit test, but that 'kind' is properly exposed as an instance member
        const sourceSeal = Transformer.sealSourceDirectory(d`src`);
        const kind = sourceSeal.kind;
    }
}
