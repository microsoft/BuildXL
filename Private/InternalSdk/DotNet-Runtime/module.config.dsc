// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "DotNet-Runtime.win-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`DotNet-Runtime.win-x64.dsc`]
});

module({
    name: "DotNet-Runtime.osx-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`DotNet-Runtime.osx-x64.dsc`]
});

module({
    name: "DotNet-Runtime.linux-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`DotNet-Runtime.linux-x64.dsc`]
});
