// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

export {BuildXLSdk};

// Net472 is required because Test.BuildXL.Processes (which references this module) builds for
// DefaultQualifierWithNet472 to test the net472 BuildXL.Processes.dll shipped as a NuGet package
// for MSBuild (dotnet/msbuild DetouredNodeLauncher). When that dependency is migrated, this
// qualifier can be narrowed to DefaultQualifier.
export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;
