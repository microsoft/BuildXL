// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";
import * as Deployment from "Sdk.Deployment";


namespace Sdk.DeploymentTests {

    function expectFile(result: Deployment.FlattenedResult, target: RelativePath, file: File) {
        Assert.areEqual(file, result.flattenedFiles.get(target).file);
    }

    function expectCount(result: Deployment.FlattenedResult, count: number) {
        Assert.areEqual(count, result.flattenedFiles.count());
    }

    @@Testing.unitTest()
    export function flattenTwoFilesFromSameFolder() {
        let result = Deployment.flatten(
            { contents: [f`a.txt`, f`b.txt`]}
        );

        expectCount(result, 2);
        expectFile(result, r`a.txt`, f`a.txt`);
        expectFile(result, r`b.txt`, f`b.txt`);
    }

    @@Testing.unitTest()
    export function flattenWithSubFolder() {
        let result = Deployment.flatten(
            { contents: [
                f`a.txt`, 
                {
                    subfolder: "sub",
                    contents: [f`a.txt`, f`b.txt`]
                }
                ]
            }
        );

        expectCount(result, 3);
        expectFile(result, r`a.txt`, f`a.txt`);
        expectFile(result, r`sub/a.txt`, f`a.txt`);
        expectFile(result, r`sub/b.txt`, f`b.txt`);
    }

    @@Testing.unitTest()
    export function flattenTwoFilesFromDifferentFolder() {
        let result = Deployment.flatten(
            { contents: [f`a.txt`, f`folder/b.txt`]}
        );

        expectCount(result, 2);
        expectFile(result, r`a.txt`, f`a.txt`);
        expectFile(result, r`b.txt`, f`folder/b.txt`);
    }

    @@Testing.unitTest()
    export function flattenTwoFilesFromDifferentFolderSameName() {
        Testing.expectFailure( () => 
            {
                let result = Deployment.flatten(
                    { contents: [f`a.txt`, f`folder/a.txt`]}
                );
            },
            "\\a.txt' and file '",
            "\\folder\\a.txt' to the same location: 'r`a.txt`"  
        );
    }

    @@Testing.unitTest()
    export function flattenTwoFilesTakeA() {
        let result = Deployment.flatten(
            { contents: [f`a.txt`, f`folder/a.txt`]},
            (targetFile: RelativePath, sourceA: Deployment.DeployedFileWithProvenance, sourceB: Deployment.DeployedFileWithProvenance) => "takeA"
        );

        expectCount(result, 1);
        expectFile(result, r`a.txt`, f`a.txt`);

    }

    @@Testing.unitTest()
    export function flattenTwoFilesTakeB() {
        let result = Deployment.flatten(
            { contents: [f`a.txt`, f`folder/a.txt`]},
            (targetFile: RelativePath, sourceA: Deployment.DeployedFileWithProvenance, sourceB: Deployment.DeployedFileWithProvenance) => "takeB"
        );

        expectCount(result, 1);
        expectFile(result, r`a.txt`, f`folder/a.txt`);
    }
}
