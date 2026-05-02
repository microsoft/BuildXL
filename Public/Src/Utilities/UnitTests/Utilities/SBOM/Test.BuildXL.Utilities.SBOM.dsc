// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import {Transformer} from "Sdk.Transformers";

namespace Core.SBOM {
    export declare const qualifier: BuildXLSdk.Net8Qualifier;

    // The SBOM utility tests don't have a conceptual need to be a separate dll, but latest version of SBOM packages are net8.0+ only.
    // TODO: merge these tests back into the regular test utilities dll. CODESYNC: Public\Src\Utilities\UnitTests\Utilities\Test.BuildXL.Utilities.dsc
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Utilities.SBOM",
        allowUnsafeBlocks: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, importFrom("BuildXL.Utilities").SBOMUtilities.dll),
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, importFrom("Microsoft.Sbom.Contracts").pkg),
        ],
    });
}
