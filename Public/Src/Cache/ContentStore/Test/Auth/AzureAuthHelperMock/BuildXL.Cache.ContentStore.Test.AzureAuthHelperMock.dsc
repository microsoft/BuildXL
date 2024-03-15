// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
import * as ManagedSdk from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";

namespace Test.AzureAuthHelperMock {
    export declare const qualifier : BuildXLSdk.AllSupportedQualifiers;

    @@public
    export const exe = BuildXLSdk.executable({
        assemblyName: "azure-auth-helper",
        sources: globR(d`.`,"*.cs"),
        skipDocumentationGeneration: true,
        references: [],
    });
}