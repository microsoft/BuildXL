// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

export declare const qualifier: BuildXLSdk.FullFrameworkQualifier;

@@public
export const deployment = BuildXLSdk.isDropToolingEnabled ? DropDaemon.deployment : undefined;

@@public
export const evaluationOnlyDeployment = BuildXLSdk.isDropToolingEnabled ? DropDaemon.evaluationOnlyDeployment : undefined;

@@public
export const exe = BuildXLSdk.isDropToolingEnabled ? DropDaemon.exe : undefined;

@@public
export function selectDeployment(evaluationOnly: boolean) {
    return BuildXLSdk.isDropToolingEnabled ? DropDaemon.selectDeployment(evaluationOnly) : undefined;
} 
