// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Cmd} from "Sdk.Transformers";

namespace Test.Tool.DropDaemon {
    export const dll = !BuildXLSdk.Flags.isMicrosoftInternal ? undefined : BuildXLSdk.test({
        assemblyName: "Test.Tool.DropDaemon",
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Tools.DropDaemon").exe,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("ArtifactServices.App.Shared").pkg,
            importFrom("ItemStore.Shared").pkg,
            importFrom("Drop.App.Core").pkg,
            importFrom("Drop.Client").pkg,
            importFrom("Drop.RemotableClient.Interfaces").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesSharedPkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
        ],
    });
}
