// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function isTrue() {
        Assert.isTrue(
            true
        );
        Assert.isTrue(
            true,
            "This should pass"
        );
    }
    
    @@Testing.unitTest()
    export function isFalse() {
        Assert.isFalse(
            false
        );
        Assert.isFalse(
            false,
            "This should pass"
        );
    }
    
    @@Testing.unitTest()
    export function areEqualWithMessage() {
        Assert.areEqual(0, 0, "Message");
    }
    
    @@Testing.unitTest()
    export function areEqualString() {
        Assert.areEqual("", "");
        Assert.areEqual("a A", "a A");
    }
    
    @@Testing.unitTest()
    export function areEqualNumber() {
        Assert.areEqual(0, 0);
        Assert.areEqual(1, 1);
        Assert.areEqual(
            -100,
            -100
        );
        Assert.areEqual(33333, 33333);
    }
    
    @@Testing.unitTest()
    export function areEqualPath() {
        Assert.areEqual(p`.`, p`.`);
        Assert.areEqual(p`folder/file.txt`, p`folder/file.txt`);
    }
    
    @@Testing.unitTest()
    export function areEqualPathAtom() {
        Assert.areEqual(a`atom`, a`atom`);
        Assert.areEqual(a`atom`, p`folder/atom`.name);
    }
    
    @@Testing.unitTest()
    export function areEqualFile() {
        Assert.areEqual(f`./file`, f`./file`);
        Assert.areEqual(f`folder/file.txt`, f`folder/file.txt`);
    }
    
    @@Testing.unitTest()
    export function areEqualDirectory() {
        Assert.areEqual(d`.`, d`.`);
        Assert.areEqual(d`folder/subFolder`, d`folder/subFolder`);
    }
    
    @@Testing.unitTest()
    export function areEqualRelativePath() {
        Assert.areEqual(r`folder`, r`folder`);
        Assert.areEqual(r`folder/subFolder`, r`folder/subFolder`);
    }
    
    @@Testing.unitTest()
    export function areEqualObject() {
        const x = {a: 1};
        Assert.areEqual(x, x);
        Assert.areEqual(undefined, undefined);
        Assert.notEqual({a: 1}, {a: 1});
    }
}
