// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

config({
    modules: globR(d`Src`, "module.config.dsc"),
    resolvers: [
        {
            kind: "DScript",
            modules: [
                ...globR(d`${Environment.getPathValue("BUILDXL_BIN_DIRECTORY")}/Sdk`, "package.config.dsc"),
                ...globR(d`${Environment.getPathValue("BUILDXL_BIN_DIRECTORY")}/Sdk`, "module.config.dsc"),
                ...globR(d`${Environment.getPathValue("BUILDXL_BIN_DIRECTORY")}/Sdk`, "module.config.bm"),
            ]
        },
    ],
    mounts: [
        {
            name: PathAtom.create("BinDirectorySdk"),
            path: Environment.hasVariable("BUILDXL_BIN_DIRECTORY") ? p`${Environment.getPathValue("BUILDXL_BIN_DIRECTORY")}/Sdk` : p`Out`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
        },
    ],
});
