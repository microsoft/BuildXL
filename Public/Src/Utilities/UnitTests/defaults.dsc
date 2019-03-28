// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import { NetFx } from "Sdk.BuildXL";

export {BuildXLSdk, NetFx};
export declare const qualifier: BuildXLSdk.DefaultQualifier;

@@public
export const testProcessExe = TestProcess.exe;

@@public
export const dummyWaiterExe = DummyWaiter.exe;
