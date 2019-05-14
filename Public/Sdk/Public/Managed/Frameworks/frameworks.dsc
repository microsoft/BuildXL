// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Shared from "Sdk.Managed.Shared";

export declare const qualifier : Shared.TargetFrameworks.All;

@@public
export const framework : Shared.Framework = (() => {
    switch (qualifier.targetFramework)
    {
        case "net451":
            return importFrom("Sdk.Managed.Frameworks.Net451").framework;
        case "net461":
            return importFrom("Sdk.Managed.Frameworks.Net461").framework;
        case "net472":
            return importFrom("Sdk.Managed.Frameworks.Net472").framework;
        case "netcoreapp2.2":
            return importFrom("Sdk.Managed.Frameworks.NetCoreApp2.2").framework;
        case "netcoreapp3.0":
            return importFrom("Sdk.Managed.Frameworks.NetCoreApp3.0").framework;
        case "netstandard2.0":
            return importFrom("Sdk.Managed.Frameworks.NetStandard2.0").framework;
        default:
            return undefined; // Let callers handle not having a given .NET framework
    }
})();
