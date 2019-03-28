// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "NodeJs.win-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`NodeJs.win-x64.dsc`]
});

module({
    name: "NodeJs.osx-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`NodeJs.osx-x64.dsc`]
});

module({
    name: "NodeJs.linux-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`NodeJs.linux-x64.dsc`]
});
