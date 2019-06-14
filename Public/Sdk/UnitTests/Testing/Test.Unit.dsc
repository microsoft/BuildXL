// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function unitGetIsNotUndefined() {
        const unit = Unit.unit();
        Assert.isTrue(
            unit !== undefined
        );
    }
    
    @@Testing.unitTest()
    export function unitIsSingleton() {
        const unit1 = Unit.unit();
        const unit2 = Unit.unit();
        Assert.isTrue(
            unit1 === unit2
        );
    }
}
