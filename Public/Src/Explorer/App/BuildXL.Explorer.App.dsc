// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Electron from "Sdk.NodeJs.Electron";
import * as Branding from "BuildXL.Branding";

namespace App {
    export declare const qualifier: {targetRuntime: "win-x64"};

    @@public
    export const app = BuildXLSdk.Flags.excludeBuildXLExplorer ? undefined : Electron.publish({
        name: "bxp",
        winIcon: Branding.iconFile,
        projectFolder: d`.`,
        authenticatedPackageFeed: importFrom("Sdk.BuildXL").Flags.isMicrosoftInternal
            ? "pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/npm/registry"
            : undefined,
    });
}
