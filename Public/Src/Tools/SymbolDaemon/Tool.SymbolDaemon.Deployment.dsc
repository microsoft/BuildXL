// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";

const specs = [
    f`Tool.SymbolDaemonRunner.dsc`,        
    f`Tool.SymbolDaemonInterfaces.dsc`,        
    {
        file: f`LiteralFiles/package.config.dsc.literal`, 
        targetFileName: a`package.config.dsc`},
    {
        file: f`LiteralFiles/Tool.SymbolDaemonTool.dsc.literal`,
        targetFileName: a`Tool.SymbolDaemonTool.dsc`,
    }];

@@public
export const deployment: Deployment.Definition = !BuildXLSdk.isSymbolToolingEnabled ? undefined : {
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
export const evaluationOnlyDeployment: Deployment.Definition = !BuildXLSdk.isSymbolToolingEnabled ? undefined : {
    contents: specs
};

@@public
export function selectDeployment(evaluationOnly: boolean) : Deployment.Definition {
    return evaluationOnly? evaluationOnlyDeployment : deployment;
}