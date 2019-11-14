// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "ArtifactServices.App.Shared", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`ArtifactServices.App.Shared.dsc`]
});

module({
    name: "ArtifactServices.App.Shared.Cache", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`ArtifactServices.App.Shared.Cache.dsc`]
});

module({
    name: "Drop.App.Core", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Drop.App.Core.dsc`]
});

module({
    name: "Drop.Client", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Drop.Client.dsc`]
});

module({
    name: "Drop.RemotableClient", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Drop.RemotableClient.dsc`]
});

module({
    name: "Drop.RemotableClient.Interfaces", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Drop.RemotableClient.Interfaces.dsc`]
});

module({
    name: "ItemStore.Shared", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`ItemStore.Shared.dsc`]
});

module({
    name: "Symbol.App.Core", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Symbol.App.Core.dsc`]
});

module({
    name: "Symbol.Client", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Symbol.Client.dsc`]
});

module({
    name: "Microsoft.Windows.Debuggers.SymstoreInterop", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Microsoft.Windows.Debuggers.SymstoreInterop.dsc`]
});
