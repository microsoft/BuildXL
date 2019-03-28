// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

import * as MsVcpp from "VisualCppTools.Community.VS2017Layout";

export declare const qualifier: {
    platform: "x86" | "x64";
};

// Folders
const rootFolder = d`${MsVcpp.Contents.all.root}/lib/native`;
const toolFolder =  d`${rootFolder}/bin/${"Host" + qualifier.platform}/${qualifier.platform}`;
const mfcLibFolder = d`${rootFolder}/atlmfc/lib/${qualifier.platform}`;
const libFolder = d`${rootFolder}/lib/${qualifier.platform}`;

const visualCppDeploymentTemplate = {
    prepareTempDirectory: true,
    runtimeDependencies: [
        f`${toolFolder}/VCTIP.exe`,
        f`${toolFolder}/msobj140.dll`,
        f`${toolFolder}/mspft140.dll`,
        f`${toolFolder}/mspdb140.dll`,
        f`${toolFolder}/mspdbsrv.exe`,
        f`${toolFolder}/mspdbcore.dll`,
    ],
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
};

@@public
export const cvtResDeployment: Transformer.ToolDefinition = visualCppDeploymentTemplate.merge<Transformer.ToolDefinition>({
    exe: f`${toolFolder}/CvtRes.exe`,
    description: "Microsoft Resource to Object Converter",
    runtimeDependencies: [
        f`${toolFolder}/CvtRes.exe`,
        f`${toolFolder}/1033/CvtResUI.dll`,
    ],
    dependsOnWindowsDirectories: true,
});

@@public
export const clDeployment: Transformer.ToolDefinition = visualCppDeploymentTemplate.merge<Transformer.ToolDefinition>({
    exe: f`${toolFolder}/cl.exe`,
    description: "Microsoft C/C++ compiler",
    runtimeDependencies: [
        f`${toolFolder}/c1.dll`,
        f`${toolFolder}/c1xx.dll`,
        f`${toolFolder}/c2.dll`,
        f`${toolFolder}/1033/clui.dll`,
        f`${toolFolder}/1033/mspft140ui.dll`,
    ],
    dependsOnWindowsDirectories: true,
    dependsOnAppDataDirectory: true,
});

@@public
export const linkDeployment: Transformer.ToolDefinition = visualCppDeploymentTemplate.merge<Transformer.ToolDefinition>({
    exe: f`${toolFolder}/Link.exe`,
    description: "Microsoft Linker",
    runtimeDependencies: [
        f`${toolFolder}/mspdbsrv.exe`,
        f`${toolFolder}/1033/LinkUI.dll`,
        ...cvtResDeployment.runtimeDependencies,
        ...clDeployment.runtimeDependencies,
    ],
    dependsOnWindowsDirectories: true,
});

@@public
export const libDeployment: Transformer.ToolDefinition = visualCppDeploymentTemplate.merge<Transformer.ToolDefinition>({
    exe: f`${toolFolder}/Lib.exe`,
    description: "Microsoft Library Manager",
    runtimeDependencies: [
        f`${toolFolder}/Link.exe`,
        f`${toolFolder}/CvtRes.exe`,
        ...linkDeployment.runtimeDependencies,
        ...clDeployment.runtimeDependencies,
    ],
    dependsOnWindowsDirectories: true,
});

export namespace AtlMfc {
    @@public
    export const include: StaticDirectory = Transformer.sealDirectory(
        d`${rootFolder}/atlmfc/include`,
        globR(d`${rootFolder}/atlmfc/include`, "*"));

    @@public
    export const lib: StaticDirectory = Transformer.sealDirectory(mfcLibFolder, globR(mfcLibFolder, "*"));
}

@@public
export const include: StaticDirectory = Transformer.sealDirectory(
    d`${rootFolder}/include`,
    globR(d`${rootFolder}/include`, "*"));
    
@@public
export const lib: StaticDirectory = Transformer.sealDirectory(libFolder, globR(libFolder, "*"));
