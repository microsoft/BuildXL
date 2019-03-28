// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Bond.NET", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [
        f`bond.net.dsc`,
    ]
});

module({
    name: "Bond.Core.NET", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [
        f`bond.core.net.dsc`,
    ]
});

module({
    name: "Bond.Rpc.NET", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [
        f`bond.rpc.net.dsc`,
    ]
});
