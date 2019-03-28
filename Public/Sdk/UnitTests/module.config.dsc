// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// These are the unittests for the Sdk's
// When we have a 'module publish' feature that will create a binary form of a module we can
// move these unittests into the actual module as a non-public const.

module({
    name: "Test.Sdk",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [
        f`module.dsc`,
        ...globR(d`.`, "SdkTest.*.dsc"),
    ],
});
