// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";

const specs = [
    f`Tool.MaterializationDaemonRunner.dsc`,
    f`Tool.MaterializationDaemonInterfaces.dsc`,
    {
        file: f`LiteralFiles/package.config.dsc.literal`, 
        targetFileName: a`package.config.dsc`},
    {
        file: f`LiteralFiles/Tool.MaterializationDaemonTool.dsc.literal`,
        targetFileName: a`Tool.MaterializationDaemonTool.dsc`,
    }];

@@public
export const deployment: Deployment.Definition = !BuildXLSdk.isDaemonToolingEnabled ? undefined : {
    contents: [
        ...specs,
        {
            subfolder: "bin",
            contents: [
                exe,
            ],
        },
    ],
};

@@public
export const evaluationOnlyDeployment: Deployment.Definition = !BuildXLSdk.isDaemonToolingEnabled ? undefined : {
    contents: specs
};

@@public
export function selectDeployment(evaluationOnly: boolean) : Deployment.Definition {
    return evaluationOnly? evaluationOnlyDeployment : deployment;
}