// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";
import {Hashing} from "Sdk.Hashing";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function HashEqual(){
        const content = "hello BuildXL!";
        const expectedHash = "9508303A65753E8B728004EAA7A30D7FC94CF0656DB2755CF4322B3CD0A7853A";
        const hash = Hashing.sha256(content);
        Assert.areEqual(hash,expectedHash);
    }

    @@Testing.unitTest()
    export function HashNotEqual(){
        const content = "hello BuildXL!";
        const unexpectedHash = "hey dawg!";
        const hash = Hashing.sha256(content);
        Assert.notEqual(hash,unexpectedHash);
    }

    @@Testing.unitTest()
    export function HashUndefined(){
        Testing.expectFailure(
            () => 
            {
                const content = undefined;
                const hash = Hashing.sha256(content);
            },
            {
                code: 9327,
                content: "Expecting type(s) 'string' for argument 1, but got 'undefined' of type 'undefined'.",
            }
        );       
    }
}
