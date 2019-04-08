// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";

namespace Sdk.Tests {
    const testMountName = "testMountName";
    const testBuildParameter = "buildParameter";
    
    @@Testing.unitTest()
    export function testExpectFailure() {
        Testing.expectFailure(
            () => Context.getMount(testMountName),
            {
                // TODO: Using hard-coded number is unreliable because if one modifies LogEventId.cs, then you need to update the code as well.
                code: 9366,
                content: "Mount with name 'testMountName' was not found. Legal mounts are: '",
            }
        );
    }
    
    @@Testing.unitTest()
    export function testAddMountPoint() {
        Assert.isFalse(
            Context.hasMount(testMountName)
        );
        Testing.setMountPoint({
            name: a`${testMountName}`,
            path: p`.`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: true,
            trackSourceFileChanges: true,
        });
        Assert.isTrue(
            Context.hasMount(testMountName)
        );
        const mount = Context.getMount(testMountName);
        Assert.areEqual(a`${testMountName}`, mount.name);
        Assert.areEqual(p`.`, mount.path);
    }
    @@Testing.unitTest()
    export function testRemoveMountPoint() {
        Assert.isFalse(
            Context.hasMount(testMountName)
        );
        Testing.setMountPoint({
            name: a`${testMountName}`,
            path: p`.`,
            isReadable: true,
            isWritable: true,
            isSystem: true,
            isScrubbable: true,
            trackSourceFileChanges: true,
        });
        Assert.isTrue(
            Context.hasMount(testMountName)
        );
        Testing.removeMountPoint(testMountName);
        Assert.isFalse(
            Context.hasMount(testMountName)
        );
    }
    @@Testing.unitTest()
    export function testSetBuildParameter() {
        Assert.isFalse(
            Environment.hasVariable(testBuildParameter)
        );
        Testing.setBuildParameter(testBuildParameter, "Value1");
        Assert.isTrue(
            Environment.hasVariable(testBuildParameter)
        );
        Assert.areEqual(
            "Value1",
            Environment.getStringValue(testBuildParameter)
        );
        Testing.setBuildParameter(testBuildParameter, "Value2");
        Assert.isTrue(
            Environment.hasVariable(testBuildParameter)
        );
        Assert.areEqual(
            "Value2",
            Environment.getStringValue(testBuildParameter)
        );
    }
    @@Testing.unitTest()
    export function testRemoveBuildParameter() {
        Assert.isFalse(
            Environment.hasVariable(testBuildParameter)
        );
        Testing.setBuildParameter(testBuildParameter, "Value1");
        Assert.isTrue(
            Environment.hasVariable(testBuildParameter)
        );
        Testing.removeBuildParameter(testBuildParameter);
        Assert.isFalse(
            Environment.hasVariable(testBuildParameter)
        );
    }
}
