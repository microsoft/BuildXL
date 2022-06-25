// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import { NetFx } from "Sdk.BuildXL";

const temporarySdkSymbolsNextToEngineFolder = d`${Context.getBuildEngineDirectory()}/Sdk/Sdk.Symbols/bin`;
const temporarySymbolDaemonTool : Transformer.ToolDefinition = {
    exe: f`${temporarySdkSymbolsNextToEngineFolder}/SymbolDaemon.exe`,
    runtimeDirectoryDependencies: [
        Transformer.sealSourceDirectory({
            root: temporarySdkSymbolsNextToEngineFolder,
            include: "allDirectories",
        }), 
    ],
    untrackedDirectoryScopes: [
        Context.getUserHomeDirectory(),
        d`${Context.getMount("ProgramData").path}`,
    ],
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
    prepareTempDirectory: true,
};

@@public
export const tool = !BuildXLSdk.isSymbolToolingEnabled 
    ? undefined
    : temporarySymbolDaemonTool;
    //: BuildXLSdk.deployManagedTool({
    //    tool: exe,
    //    options: toolTemplate,
    //});