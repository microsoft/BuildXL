// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";

export declare const qualifier: BuildXLSdk.Net9Qualifier;

const specs = [
    f`Tool.CloudTestClientRunner.APIs.dsc`,
    f`Tool.CloudTestClientRunner.Helpers.dsc`,
    {file: f`LiteralFiles/module.config.dsc.literal`, targetFileName: a`module.config.dsc`},
];

@@public
export const deployment: Deployment.Definition = {
    contents: [
        ...specs,
        {subfolder: "bin", contents: [importFrom("BuildXL.Tools").CloudTestClient.tool]},
    ],
};

@@public
export const evaluationOnlyDeployment: Deployment.Definition = {
    contents: specs
};

@@public
export function selectDeployment(evaluationOnly: boolean) : Deployment.Definition {
    return BuildXLSdk.Flags.isMicrosoftInternal 
        ? evaluationOnly 
            ? evaluationOnlyDeployment 
            : deployment
        : undefined;
}