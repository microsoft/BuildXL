// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Shared from "Sdk.Managed.Shared";
import * as Deployment from "Sdk.Deployment";

export declare const qualifier: {targetFramework: "netstandard2.0"};

const defaultAssemblies: Shared.Assembly[] = createDefaultAssemblies();

@@public
export const framework : Shared.Framework = {
    targetFramework: qualifier.targetFramework,

    supportedRuntimeVersion: "v2.0",
    assemblyInfoTargetFramework: ".NETStandard,Version=v2.0",
    assemblyInfoFrameworkDisplayName: ".NET Standard",

    standardReferences: defaultAssemblies,

    requiresPortablePdb: true,

    runtimeConfigStyle: "none",
    
    conditionalCompileDefines: [
        "NET_CORE",
        "NET_STANDARD",
        "NET_STANDARD_20",
    ],
};

function createDefaultAssemblies() : Shared.Assembly[] {
    const pkgContents = importFrom("NETStandard.Library").Contents.all;
    const netcoreAppPackageContents = pkgContents.contents;
    const dlls = netcoreAppPackageContents.filter(file => file.hasExtension && file.extension === a`.dll`);
    return dlls.map(file  => Shared.Factory.createAssembly(pkgContents, file, "netstandard2.0", [], true));
}
