// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "PowerShell.Core.win-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`PowerShell.Core.win-x64.dsc`]
});

module({
    name: "PowerShell.Core.osx-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`PowerShell.Core.osx-x64.dsc`]
});

module({
    name: "PowerShell.Core.linux-x64", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`PowerShell.Core.linux-x64.dsc`]
});
