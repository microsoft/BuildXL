// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";

export declare const qualifier : {
    targetFramework: "netcoreapp3.1" | "netstandard2.0" | "net472" ,
    targetRuntime: "osx-x64" | "win-x64"
};

// Any qualifier will do here - we only want to directly access the contents.
const pkgContents = importFrom("System.Data.SQLite.Core").Contents.all;

function getInteropFiles() : File | Deployment.Definition {
    switch (qualifier.targetFramework)
    {
        case "netcoreapp3.1":
        case "netstandard2.0":
            return <Deployment.Definition>{
                contents: [
                    // add the entire 'runtimes' folder
                    { subfolder: a`runtimes`, contents: [ Deployment.createFromDisk(d`${pkgContents.root}/runtimes`) ] },
                    // pick the interop assembly from the corresponding target runtime folder 
                    pkgContents.getFile(r`runtimes/${qualifier.targetRuntime}/native/netstandard2.0/SQLite.Interop.dll`)
                ]
            };
        case "net472":
            return pkgContents.getFile("build/net46/x64/SQLite.Interop.dll");
        default:
            Contract.fail("Unsupported target framework for x64 SQLite.Interop.dll");
    }
}

@@public
export const runtimeLibs : Deployment.DeployableItem = getInteropFiles();
