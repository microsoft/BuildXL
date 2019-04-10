// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";

export const explicitSemanticVersion = "0.1.0";

export declare const qualifier: {};

@@public
export const company = "Microsoft";

@@public
export const shortProductName = "BuildXL";

@@public
export const longProductName = "Microsoft (R) Build Accelerator";

@@public
export const shortScriptName = "DScript";

@@public
export const mainExecutableName = "bxl.exe";

@@public
export const analyzerExecutableName = "bxlAnalyzer.exe";

@@public
export const iconFile = f`BuildXL.ico`;

@@public
export const pngFile = f`BuildXL.png`;

@@public
export const copyright = "© Microsoft Corporation. All rights reserved.";

@@public 
export const semanticVersion = Environment.hasVariable("[BuildXL.Branding]SemanticVersion")
    ? Environment.getStringValue("[BuildXL.Branding]SemanticVersion")
    : explicitSemanticVersion;

@@public
export const prereleaseTag = "20190329.14.2";

@@public
export const version = prereleaseTag
    ? semanticVersion + "-" + prereleaseTag 
    : semanticVersion;

@@public
export const sourceIdentification = Environment.hasVariable("[BuildXL.Branding]SourceIdentification")
    ? Environment.getStringValue("[BuildXL.Branding]SourceIdentification")
    : "Developer Build";

@@public
export const productDescription = `${longProductName}. Build: ${version}, Version: [${sourceIdentification}]`;
