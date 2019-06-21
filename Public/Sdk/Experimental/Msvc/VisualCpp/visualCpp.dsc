// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

export declare const qualifier: {
    platform: "x86" | "x64";
};

const pkgContents = importFrom("VisualCppTools.Community.VS2017Layout").Contents.all;
const rootFolder = r`lib/native`;

@@public
export const cvtResTool = createMsvcTool(a`CvtRes.exe`, "Microsoft Resource to Object Converter");

@@public
export const clTool = createMsvcTool(a`cl.exe`, "Microsoft C/C++ compiler");

@@public
export const linkTool = createMsvcTool(a`Link.exe`, "Microsoft Linker");

@@public
export const libTool = createMsvcTool(a`Lib.exe`, "Microsoft Library Manager");

export namespace AtlMfc {
    @@public
    export const include = pkgContents.ensureContents({subFolder: r`${rootFolder}/atlmfc/include`});

    @@public
    export const lib = pkgContents.ensureContents({subFolder: r`${rootFolder}/atlmfc/lib/${qualifier.platform}`});
}

@@public
export const include = pkgContents.ensureContents({subFolder: r`${rootFolder}/include`});

@@public
export const lib = pkgContents.ensureContents({subFolder: r`${rootFolder}/lib/${qualifier.platform}`});

// narrowed down sealed directory with just the tools folder
const toolContents = pkgContents.ensureContents({subFolder: r`${rootFolder}/bin/${"Host" + qualifier.platform}/${qualifier.platform}`});

function createMsvcTool(exe: PathAtom, description: string) : Transformer.ToolDefinition
{
    return {
        exe: toolContents.getFile(exe),
        description: description,
        runtimeDirectoryDependencies: [
            toolContents
        ],
        prepareTempDirectory: true,
        dependsOnWindowsDirectories: true,
        dependsOnAppDataDirectory: true,
    };
}