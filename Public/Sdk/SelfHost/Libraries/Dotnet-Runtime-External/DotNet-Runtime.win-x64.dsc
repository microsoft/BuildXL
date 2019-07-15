// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import {createPublicDotNetRuntime} from "DotNet-Runtime.Common";

const v3 = <StaticDirectory>importFrom("DotNet-Runtime.win-x64.3.0.0-preview5").extracted;
const v2 = <StaticDirectory>importFrom("DotNet-Runtime.win-x64.2.2.2").extracted;

@@public
export const extracted = createPublicDotNetRuntime(v3, v2);