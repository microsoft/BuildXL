// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import {Assert, Testing} from "Sdk.Testing";
import * as Json from "Sdk.Json";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function SimpleTypes(){
        const resultFile = Json.write(p`out/out.json`, {
            s: "str",
            a: a`atom`,
            r: r`f/rel`,
            b: true,
            n: 1,
            l: [1, 2],
            h: Set.empty<string>().add("one").add("two"),
            m: Map.empty<number, {x:string}>().add(2, {x:"two"}).add(1, {x:"one"}).add(3, {x:"three"}),
            o: {
                s: "str"
            },
        });
    }

    @@Testing.unitTest()
    export function MixedPaths(){
        const resultFile = Json.write(p`out/out.json`, {
            p: p`path`,
            f: f`file`,
            d: d`dir`,
        });
    }

    @@Testing.unitTest()
    export function UnsupportedTypes(){
        Testing.expectFailure(
            () => 
            {
                let sealed = Transformer.sealDirectory(d`src`, [f`src/file1`, f`src/file2`]);
                Json.write(p`out.json`, {
                    s: sealed,
                });
            },
            {
                code: 9408,
                content: "Encountered value of type 'FullStaticContentDirectory'. This type is not supported to be serialized to Json.",
            }
        );       
    }
}
