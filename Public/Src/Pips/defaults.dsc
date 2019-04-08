// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import { NetFx } from "Sdk.BuildXL";

export {BuildXLSdk, NetFx};

// This code has to support Net451 because CloudBuild relies on this code from Net452 assemblies
export declare const qualifier : BuildXLSdk.DefaultQualifierWithNet451;
