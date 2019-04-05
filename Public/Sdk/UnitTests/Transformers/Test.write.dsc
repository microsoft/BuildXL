// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Assert, Testing} from "Sdk.Testing";
import {Transformer} from "Sdk.Transformers";

const sampleOutputPath = p`out/file`;

namespace Sdk.Tests {
    @@Testing.unitTest()
    export function writeData() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: "FileContent",
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeDataWithTags() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: "FileContent",
            tags: ["tagA", "tagZ"],
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeDataWithDescription() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: "FileContent",
            description: "CustomDescription",
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeDataNumber() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: 5,
        });
    }

    @@Testing.unitTest()
    export function writeDataPath() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: p`testFile`,
        });
    }

    @@Testing.unitTest()
    export function writeDataDir() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: d`testDir`,
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeDataAtom() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: a`atom`,
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeDataRelative() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: r`rel1/rel2`,
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeDataCompound() {
        const result = Transformer.writeData({
            outputPath: sampleOutputPath,
            contents: {
                separator: "\t",
                contents: [
                    "string",
                    99,
                    p`file`,
                    d`dir`,
                    {
                        separator: "-",
                        contents: [
                            a`atom`,
                            r`rel1/rel2`,
                        ],
                    },
                ]
            },
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeAllLines() {
        const result = Transformer.writeAllLines({
            outputPath: sampleOutputPath,
            lines: [
                "line 1",
                "line 2",
            ],
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeAllLinesWithTags() {
        const result = Transformer.writeAllLines({
            outputPath: sampleOutputPath,
            lines: [
                "line 1",
                "line 2",
            ],
            tags: ["tagA", "tagZ"],
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeAllLinesWithDescription() {
        const result = Transformer.writeAllLines({
            outputPath: sampleOutputPath,
            lines: [
                "line 1",
                "line 2",
            ],
            description: "CustomDescription",
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeAllText() {
        const result = Transformer.writeAllText({
            outputPath: sampleOutputPath,
            text: "FileContent",
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeAllTextWithTags() {
        const result = Transformer.writeAllText({
            outputPath: sampleOutputPath,
            text: "FileContent",
            tags: ["tagA", "tagZ"],
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }

    @@Testing.unitTest()
    export function writeAllTextWithDescription() {
        const result = Transformer.writeAllText({
            outputPath: sampleOutputPath,
            text: "FileContent",
            description: "CustomDescription",
        });
        Assert.areEqual(result.path, sampleOutputPath);
    }
}