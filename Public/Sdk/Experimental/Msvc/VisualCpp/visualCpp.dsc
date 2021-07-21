// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

export declare const qualifier: {
    platform: "x86" | "x64";
};

const isInternal = Environment.getFlag("[Sdk.BuildXL]microsoftInternal"); // Indicates whether to use the MSVC nuget package
const pkgContents = getMsvcPackage();
const rootFolder = isInternal ? r`lib/native` : r`.`;

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

/**
 * Gets a list of surviving child processes that cl.exe may create. NOTE: Do not add any
 * processes here that write meaningful outputs to disk.
 * VCTIP.EXE - "VC++ Technology Improvement Program" uploader used for telemetry.
 */
@@public
export const clToolBreakawayProcesses : PathAtom[] = [a`VCTIP.EXE`];

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
        untrackedDirectoryScopes: [
            d`${Context.getMount("ProgramData").path}/microsoft/netFramework/breadcrumbStore`,
            // cl.exe or child processes will create this directory if it doesn't exist
            // Then it will write MachineStorage.dat and MachineStorage.dat.bak to it.
            d`${Context.getMount("ProgramData").path}/Microsoft Visual Studio`,
            // Temporary state files accessed by VCTIP.EXE
            d`${Context.getMount("ProgramData").path}/Microsoft/VisualStudio/Packages`,
        ],
        runtimeDependencies: [
            f`${Context.getMount("ProgramData").path}/Microsoft/VisualStudio/Setup/${qualifier.platform}/Microsoft.VisualStudio.Setup.Configuration.Native.dll`,
        ],
    };
}

/** 
 * When building internally, returns the VisualCppTools.Internal.VS2017Layout package.
 * When building externally, search for the Visual Studio 2017 build tools directory.
 */
function getMsvcPackage() : StaticDirectory {
    // The VisualCppTools.Community.VS2017Layout package has been deprecated for external users.
    // Due to this, when building externally, the Visual C++ build tools must be installed manually.
    // Please see the BuildXL README on how to build externally.
    if (isInternal) {
        return importFrom("VisualCppTools.Internal.VS2017Layout").Contents.all;
    }
    else {
        let msvcVersions = [
            "14.29.30037"
        ];

        // ADO will set this variable if the version above is not installed
        if (Environment.hasVariable("MSVC_VERSION")) {
            msvcVersions = msvcVersions.push(Environment.getStringValue("MSVC_VERSION"));
        }

        const buildToolsDirectories = [
            d`${Context.getMount("ProgramFilesX86").path}/Microsoft Visual Studio/2019/Enterprise/VC/Tools/MSVC`,
            d`${Context.getMount("ProgramFilesX86").path}/Microsoft Visual Studio/2019/BuildTools/VC/Tools/MSVC`,
        ];

        for (let buildToolsDirectory of buildToolsDirectories)
        {
            for (let version of msvcVersions)
            {
                const dir = d`${buildToolsDirectory.path}/${version}`;

                if (Directory.exists(dir)) {
                    return Transformer.sealDirectory(dir, globR(dir, "*"));
                }
            }
        }

        Contract.fail(`Prerequisite Visual Studio 2017 build tools not found at any of the following locations: '${buildToolsDirectories}'. Please see BuildXL/Documentation/Wiki/DeveloperGuide.md on how to acquire these tools for building externally.`);
    }
}