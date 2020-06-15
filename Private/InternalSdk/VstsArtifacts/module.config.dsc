// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Microsoft.VisualStudio.Services.ArtifactServices.Shared", 
    projects: [f`Microsoft.VisualStudio.Services.ArtifactServices.Shared.dsc`]
});

module({
    name: "Microsoft.VisualStudio.Services.BlobStore.Client", 
    projects: [f`Microsoft.VisualStudio.Services.BlobStore.Client.dsc`]
});

module({
    name: "Microsoft.VisualStudio.Services.InteractiveClient", 
    projects: [f`Microsoft.VisualStudio.Services.InteractiveClient.dsc`]
});
