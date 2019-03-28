// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Microsoft.VisualStudio.Services.ArtifactServices.Shared", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Microsoft.VisualStudio.Services.ArtifactServices.Shared.dsc`]
});

module({
    name: "Microsoft.VisualStudio.Services.BlobStore.Client", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Microsoft.VisualStudio.Services.BlobStore.Client.dsc`]
});

module({
    name: "Microsoft.VisualStudio.Services.InteractiveClient", 
    nameResolutionSemantics: NameResolutionSemantics.implicitProjectReferences,
    projects: [f`Microsoft.VisualStudio.Services.InteractiveClient.dsc`]
});
