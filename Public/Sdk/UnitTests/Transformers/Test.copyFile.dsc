// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";
import {Transformer} from "Sdk.Transformers";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function copyFileWithoutTags() {
        Transformer.copyFile(f`src/fromFile`, p`out/toFile`);
    }

    @@Testing.unitTest()
    export function copyFileWithTags() {
        Transformer.copyFile(f`src/fromFile`, p`out/toFile`, ["aTag", "zTag"]);
    }

    const dirSep = Context.getCurrentHost().os === "win" ? "\\" : "/";

    @@Testing.unitTest()
    export function cantWriteToDestination() {
        Testing.expectFailure(
            () => Transformer.copyFile(f`src/fromFile`, p`noWrite`),
            {code: 2000, content: `cantWriteToDestination${dirSep}noWrite' is under a non-writable mount`}
        );
    }
    
    @@Testing.unitTest()
    export function cantReadFromSource() {
        Testing.expectFailure(
            () => Transformer.copyFile(f`noRead/cantReadThis`, p`out/toFile`),
            {code: 2001, content: `noRead${dirSep}cantReadThis' is under a non-readable mount`}
        );
    }
}
