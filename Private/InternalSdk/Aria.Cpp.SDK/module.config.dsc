// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Aria.Cpp.SDK.win-x64",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Aria.Cpp.SDK.win-x64.dsc`]
});

module({
    name: "Aria.Cpp.SDK.osx-x64",
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Aria.Cpp.SDK.osx-x64.dsc`]
});