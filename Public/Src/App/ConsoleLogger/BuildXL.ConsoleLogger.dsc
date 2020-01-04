// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";
import * as Branding from "BuildXL.Branding";

namespace ConsoleLogger {
    export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet472;
    
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.ConsoleLogger",
        sources: globR(d`.`, "*.cs"),
        embeddedResources: [{resX: f`Strings.resx`}],
        references: [
            importFrom("BuildXL.Engine").Scheduler.dll,
            importFrom("BuildXL.Engine").ViewModel.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
        ],
        internalsVisibleTo: [
            "Test.Bxl",
        ],
    });
}
