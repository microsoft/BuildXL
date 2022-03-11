// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Sdk.Guardian",
    projects: [
        f`Tool.Guardian.dsc`,
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win", f`Tool.Guardian.ComplianceBuild.dsc`) // Only used for Cloudbuild builds
    ]
});