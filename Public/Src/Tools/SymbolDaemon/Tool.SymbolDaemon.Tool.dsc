// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import { NetFx } from "Sdk.BuildXL";

@@public
export const tool = !BuildXLSdk.isSymbolToolingEnabled 
    ? undefined 
    : BuildXLSdk.deployManagedTool({
        tool: exe,
        options: toolTemplate,
    });