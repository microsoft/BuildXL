// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";

namespace DropTool {
    // NOTE: if you change this tool definition, chances are that you should
    //       update the LiteralFiles\Tool.DropDaemon.dsc.literal file as well
    @@public
    export const tool = !BuildXLSdk.isDropToolingEnabled ? undefined : DropDaemon.tool;
}
