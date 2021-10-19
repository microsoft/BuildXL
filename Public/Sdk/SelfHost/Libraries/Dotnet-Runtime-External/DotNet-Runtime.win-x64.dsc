// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as BuildXLSdk from "Sdk.BuildXL";
import {createPublicDotNetRuntime} from "DotNet-Runtime.Common";

const isWinOs = Context.getCurrentHost().os === "win";

@@public
export const extracted = createPublicDotNetRuntime(
    isWinOs ? <StaticDirectory>importFrom("DotNet-Runtime.win-x64.3.1.19").extracted : undefined,
    isWinOs ? <StaticDirectory>importFrom("DotNet-Runtime.win-x64.2.2.2").extracted : undefined);