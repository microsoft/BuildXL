// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Sdk.Managed.Frameworks.NetStandard2.0",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [
        f`netstandard2.0.dsc`,
    ]
});
