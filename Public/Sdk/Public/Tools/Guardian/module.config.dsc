// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

module({
    name: "Sdk.Guardian",
    projects: [
        f`deployment.dsc`,
        f`Tool.Guardian.dsc`,
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win", f`Tool.Guardian.ComplianceBuild.dsc`), // Only used for Cloudbuild builds
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win", f`Tool.Guardian.EsLint.dsc`),
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win", f`Tool.Guardian.CredScan.dsc`),
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win", f`Tool.Guardian.PsScriptAnalyzer.dsc`),
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win", f`Tool.Guardian.FlawFinder.dsc`),
        ...addIf(Environment.getFlag("[Sdk.BuildXL]microsoftInternal") && Context.getCurrentHost().os === "win", f`Tool.Guardian.PoliCheck.dsc`)
    ]
});