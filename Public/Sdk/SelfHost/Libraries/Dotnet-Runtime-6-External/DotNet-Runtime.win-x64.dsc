// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {createPublicDotNetRuntime} from "DotNet-Runtime.Common";

const isWinOs = Context.getCurrentHost().os === "win";

@@public
export const extracted = createPublicDotNetRuntime(
    isWinOs ? <StaticDirectory>importFrom("DotNet-Runtime.win-x64.6.0.201").extracted : undefined, undefined);