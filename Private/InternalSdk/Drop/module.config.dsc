// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "ArtifactServices.App.Shared",
	projects: [f`ArtifactServices.App.Shared.dsc`]
});

module({
    name: "Drop.App.Core",
	projects: [f`Drop.App.Core.dsc`]
});

module({
    name: "Drop.Client",
	projects: [f`Drop.Client.dsc`]
});

module({
    name: "ItemStore.Shared",
	projects: [f`ItemStore.Shared.dsc`]
});

module({
    name: "Microsoft.Azure.Storage.Common",
	projects: [f`Microsoft.Azure.Storage.Common.dsc`]
});

module({
    name: "Microsoft.FileFormats",
	projects: [f`Microsoft.FileFormats.dsc`]
});

module({
    name: "Microsoft.SymbolStore",
	projects: [f`Microsoft.SymbolStore.dsc`]
});

module({
    name: "Microsoft.VisualStudio.Services.BlobStore.Client.Cache",
	projects: [f`Microsoft.VisualStudio.Services.BlobStore.Client.Cache.dsc`]
});

module({
    name: "Microsoft.VisualStudio.Services.Symbol.App.Core",
	projects: [f`Microsoft.VisualStudio.Services.Symbol.App.Core.dsc`]
});

module({
    name: "Microsoft.VisualStudio.Services.Symbol.App.Indexer",
	projects: [f`Microsoft.VisualStudio.Services.Symbol.App.Indexer.dsc`]
});

module({
    name: "Microsoft.Windows.Debuggers.SymstoreInterop",
	projects: [f`Microsoft.Windows.Debuggers.SymstoreInterop.dsc`]
});

module({
    name: "Symbol.Client",
	projects: [f`Symbol.Client.dsc`]
});
