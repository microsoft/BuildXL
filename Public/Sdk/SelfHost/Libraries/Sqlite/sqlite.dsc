// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";

export declare const qualifier : {
    targetFramework: "netcoreapp3.0" | "net472" | "net461" | "net451",
    targetRuntime: "osx-x64" | "win-x64"
};

// Any qualifier will do here - we only want to directly access the contents.
const SQLiteCorePackageContents = importFrom("System.Data.SQLite.Core").Contents.all;

function getInteropFile() : File {
    switch (qualifier.targetFramework)
    {
        case "netcoreapp3.0":
            return SQLiteCorePackageContents.getFile(r`runtimes/${qualifier.targetRuntime}/native/netstandard2.0/SQLite.Interop.dll`);
        case "net472":
            return SQLiteCorePackageContents.getFile("build/net46/x64/SQLite.Interop.dll");
        case "net461":
            return SQLiteCorePackageContents.getFile("build/net46/x64/SQLite.Interop.dll");
        case "net451":
            return SQLiteCorePackageContents.getFile("build/net451/x64/SQLite.Interop.dll");
        default:
            Contract.fail("Unsupported target framework for x64 SQLite.Interop.dll");
    }
}

@@public
export const runtimeLibs : Deployment.DeployableItem = getInteropFile();
