// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as BuildXLBranding from "BuildXL.Branding";

namespace Branding {

    @@public
    export const brandingManifest = Transformer.writeAllLines({
        // The filename is relied upon by Branding.cs
        outputPath: p`${Context.getNewOutputDirectory('branding')}/BuildXL.manifest`, 

        // The lines in the file are relied up by Branding.cs. Any changes here need to be synced.
        lines: [
            BuildXLBranding.shortProductName,
            BuildXLBranding.longProductName,
            BuildXLBranding.version,
            BuildXLBranding.sourceIdentification,
            BuildXLBranding.mainExecutableName,
            BuildXLBranding.analyzerExecutableName,
        ]
    });
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Utilities.Branding",
        sources: globR(d`.`, "*.cs"),
        references: [
            $.dll,
        ],
        runtimeContent: [
            brandingManifest,
        ],
    });
}