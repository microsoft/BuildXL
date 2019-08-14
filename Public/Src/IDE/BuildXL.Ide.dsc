// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import { NetFx } from "Sdk.BuildXL";

export declare const qualifier: BuildXLSdk.DefaultQualifier;

export {BuildXLSdk, NetFx};

export interface VsCodeExtensionQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "netcoreapp3.0";
    targetRuntime: "win-x64" | "osx-x64";
}

namespace LanguageService {
    export declare const qualifier : VsCodeExtensionQualifier;
}