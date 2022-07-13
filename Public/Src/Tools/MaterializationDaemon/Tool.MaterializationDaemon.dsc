// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import { NetFx } from "Sdk.BuildXL";

export declare const qualifier : BuildXLSdk.NetCoreAppQualifier;

@@public
export const exe = !BuildXLSdk.isDaemonToolingEnabled ? undefined : BuildXLSdk.executable({
    assemblyName: "MaterializationDaemon",
    rootNamespace: "Tool.MaterializationDaemon",
    sources: globR(d`.`, "*.cs"),

    references: [
        importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
        importFrom("BuildXL.Utilities.Instrumentation").Common.dll,
        importFrom("BuildXL.Utilities.Instrumentation").Tracing.dll,
        importFrom("BuildXL.Utilities").dll,
        importFrom("BuildXL.Utilities").Ipc.dll,
        importFrom("BuildXL.Utilities").Ipc.Providers.dll,
        importFrom("BuildXL.Utilities").Native.dll,
        importFrom("BuildXL.Utilities").Storage.dll,
        importFrom("BuildXL.Utilities").Collections.dll,
        importFrom("BuildXL.Tools").ServicePipDaemon.dll,

        importFrom("Newtonsoft.Json").pkg,
        
        NetFx.System.Xml.dll
    ],
});
