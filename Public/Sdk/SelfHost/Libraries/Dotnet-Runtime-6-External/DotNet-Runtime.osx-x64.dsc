// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {createPublicDotNetRuntime} from "DotNet-Runtime.Common";

@@public
export const extracted = createPublicDotNetRuntime(importFrom("DotNet-Runtime.osx-x64.6.0.101").extracted, undefined);