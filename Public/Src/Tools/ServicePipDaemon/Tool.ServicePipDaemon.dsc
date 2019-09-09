// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import { NetFx } from "Sdk.BuildXL";

namespace ServicePipDaemon {

    @@public
    export const dll = !BuildXLSdk.isDaemonToolingEnabled ? undefined : BuildXLSdk.library({
        assemblyName: "Tool.ServicePipDaemon",
        rootNamespace: "Tool.ServicePipDaemon",        
        sources: globR(d`.`, "*.cs"),
        references:[            
            importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Ipc.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
        
            importFrom("ArtifactServices.App.Shared").pkg,
            importFrom("ArtifactServices.App.Shared.Cache").pkg,
            importFrom("Drop.App.Core").pkg,
            importFrom("Drop.Client").pkg,            
            importFrom("ItemStore.Shared").pkg,
            importFrom("Microsoft.ApplicationInsights").pkg,
            importFrom("Microsoft.AspNet.WebApi.Client").pkg,
            importFrom("Microsoft.Diagnostics.Tracing.TraceEvent").pkg,
            importFrom("Microsoft.IdentityModel.Clients.ActiveDirectory").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesSharedPkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            importFrom("Microsoft.VisualStudio.Services.InteractiveClient").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("WindowsAzure.Storage").pkg,
            
        ],
        internalsVisibleTo: [
            "Test.Tool.DropDaemon",
        ]
    });
}
