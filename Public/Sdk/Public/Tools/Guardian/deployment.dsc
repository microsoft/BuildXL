// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";

@@public
export const deployment : Deployment.Definition = {
    contents: [
        f`Tool.Guardian.dsc`,
        {file: f`LiteralFiles/module.config.dsc.literal`, targetFileName: a`module.config.dsc`},
    ]
};

@@public
export const internalDeployment : Deployment.Definition = {
    contents: [
        f`Tool.Guardian.dsc`,
        f`Tool.Guardian.ComplianceBuild.dsc`,
        f`Tool.Guardian.CredScan.dsc`,
        f`Tool.Guardian.EsLint.dsc`,
        f`Tool.Guardian.PsScriptAnalyzer.dsc`,
        f`Tool.Guardian.FlawFinder.dsc`,
        f`Tool.Guardian.PoliCheck.dsc`,
        {file: f`LiteralFiles/module.config.dsc.literal`, targetFileName: a`module.config.dsc`},
    ]
};
