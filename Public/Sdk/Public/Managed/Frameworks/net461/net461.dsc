// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Shared from "Sdk.Managed.Shared";

export declare const qualifier : {targetFramework: "net461"};

@@public
export const framework : Shared.Framework = {
    targetFramework: "net461",

    supportedRuntimeVersion: "v4.0",
    assemblyInfoTargetFramework: ".NETFramework,Version=v4.6.1",

    assemblyInfoFrameworkDisplayName: ".NET Framework 4.6.1",

    standardReferences: [
        NetFx.MsCorLib.dll,
        NetFx.System.dll,
        NetFx.System.Core.dll,
        NetFx.System.Runtime.dll,
        NetFx.System.Collections.dll,
    ],

    requiresPortablePdb: false,

    runtimeConfigStyle: "appConfig",

    conditionalCompileDefines: [
        "NET_FRAMEWORK",
        "NET_FRAMEWORK_461"
    ]
};
