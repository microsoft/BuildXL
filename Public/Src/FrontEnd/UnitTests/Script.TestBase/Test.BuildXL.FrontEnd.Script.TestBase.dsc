// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

const sdkRoot = Context.getMount("SdkRoot").path;

// TODO: this alias is temporarily here because otherwise the type checker complains.
namespace Script.TestBase {
    @@public
    export const preludeFiles: File[] = glob(d`../Libs`);

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "Test.BuildXL.FrontEnd.Script.TestBase",
        sources: globR(d`.`, "*.cs"),
        references: [
            Core.dll,
            importFrom("BuildXL.FrontEnd").Script.dll,
            importFrom("BuildXL.FrontEnd").Sdk.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Collections.dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.dll,
            importFrom("BuildXL.Utilities.UnitTests").TestUtilities.XUnit.dll,
            importFrom("xunit.abstractions").withQualifier({targetFramework: "netstandard2.0"}).pkg,
        ],
        runtimeContent: [
            {
                subfolder: a`Libs`,
                contents: preludeFiles,
            },
            {
                subfolder: r`Sdk/Sdk.Transformers`,
                contents: glob(d`${sdkRoot}/Transformers`, "*.dsc"),
            },
        ],
    });
}
