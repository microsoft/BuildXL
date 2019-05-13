// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export declare const qualifier: { targetFramework: "netcoreapp3.0" | "netstandard2.0" | "net451" | "net461" | "net472"};

import * as Managed from "Sdk.Managed";

// This module is a workaround wrapper for the existing package where there doesn't exist a netstandard version

// package contents
const package = importFrom("Microsoft.VisualStudio.LanguageServer.Protocol").withQualifier({targetFramework: "net461"}).pkg;

@@public
export const pkg: Managed.ManagedNugetPackage = (() => {
    switch (qualifier.targetFramework) {
        case "netcoreapp3.0":
        case "netstandard2.0":
        case "net451":
        case "net461":
        case "net472":
            return Managed.Factory.createNugetPackage(
                package.name,
                package.version,
                package.contents,
                [
                    Managed.Factory.createBinary(package.contents, r`lib/net461/Microsoft.VisualStudio.LanguageServer.Protocol.Preview.dll`),
                ],
                [
                    Managed.Factory.createBinary(package.contents, r`lib/net461/Microsoft.VisualStudio.LanguageServer.Protocol.Preview.dll`),
                ],
                [
                    importFrom("Newtonsoft.Json").pkg
                ]
            );
        default:
            Contract.fail("Unsupported target framework");
    };
}
)();
