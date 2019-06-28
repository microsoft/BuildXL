// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";
import {Node} from "Sdk.NodeJs";
import * as Yarn from "Sdk.NodeJs.Yarn";
import * as PowerShell from "Sdk.PowerShell.Core";

// $TODO: Specialize electron mac packagaging per: https://electronjs.org/docs/tutorial/application-packaging
export declare const qualifier : {targetRuntime: "win-x64"};

// TODO: This is for the windows layout only for now. We'll need to tweak it a bit for Mac.
@@public
export function publish(args: Arguments) : Result {
    // Pull down the production modules
    const prodModules = Yarn.install({
        projectFolder: args.projectFolder,
        targetSubFolder: "prod",
        production: true,
        authenticatedPackageFeed: args.authenticatedPackageFeed,
    });
    
    // Pull down the dev-modules
    const devModules = Yarn.install({
        projectFolder: args.projectFolder,
        targetSubFolder: "dev",
        production: false,
        authenticatedPackageFeed: args.authenticatedPackageFeed,
    });
    
    // TypeScript compile the
    const compiledTypeScript = TypeScript.tsc({
        nodeModules: devModules.modulesFolder,
        projectFolder: devModules.projectFolder
    });

    const prodFolderToPack = Context.getNewOutputDirectory("electron-asarPack");
    // TODO: BuildXL lacks a way to copy opaque folders and doesn't allow for excluding of files yet, so we have to do this in powershell
    const copyHackResult1 = PowerShell.executeCommands([
        "Copy-Item -Path $Env:Param_projWithProdModules\\* -Destination $Env:Param_output -Recurse",
        "Copy-Item -Path $Env:Param_compiledTsFiles\\* -Destination $Env:Param_output\\src -Recurse -Force",
    ], {
        environmentVariables: [
            {name: "Param_projWithProdModules", value: prodModules.projectFolder },
            {name: "Param_compiledTsFiles", value: compiledTypeScript.outFolder },
            {name: "Param_output", value: prodFolderToPack },
        ],
        dependencies: [
            prodModules.projectFolder,
            prodModules.modulesFolder,
            compiledTypeScript.outFolder,
            devModules.modulesFolder,
        ],
        outputs: [
            prodFolderToPack,
        ],
    });
    const folderToPack = copyHackResult1.getOutputDirectory(prodFolderToPack);

    // Pack all production files into an asar:
    const appAsar = Asar.pack({
        nodeModules: devModules.modulesFolder,
        folderToPack: folderToPack,
        name: "app.asar",
    });

    const extractedElectron : StaticDirectory = importFrom("Electron.win-x64").extracted;

    // Ensure the executable is branded
    const electronExe = extractedElectron.getFile(r`electron.exe`);
    const renamedExe = Transformer.copyFile(electronExe, p`${Context.getNewOutputDirectory("electron-branding")}/${args.name + ".exe"}`);
    // RcEdit only runs on win32 systems for win32.
    const brandedExe = args.winIcon && qualifier.targetRuntime === "win-x64" && Context.getCurrentHost().os === "win"
        ? RcEdit.setIcon({file: renamedExe, nodeModules: devModules.modulesFolder, icon: args.winIcon}).file 
        : renamedExe;

    //  Aggregate into one folder
    // TODO: BuildXL lacks a way to copy opaque folders and doesn't allow for excluding of files yet, so we have to do this in powershell
    const finalOutput = Context.getNewOutputDirectory("electron-app");
    const copyHackResult2 = PowerShell.executeCommands([
        "Copy-Item -Path $Env:Param_inElectron\\* -Destination $Env:Param_output -Recurse",
        
        "$electronExe=[io.path]::combine($Env:Param_output, 'electron.exe')", // Powershell does not allow pathcombine inside an expression <doh>
        "Remove-Item -Path $electronExe -Force", // Remove electron.exe to be replaced by the branded exe
        
        "Copy-Item -Path $Env:Param_inBrandedExe -Destination $Env:Param_output",

        "$resourceFolder=[io.path]::combine($Env:Param_output, 'resources')",
        "$default_app=[io.path]::combine($resourceFolder, 'default_app.asar')", // Powershell does not allow pathcombine inside an expression <doh>
        "Remove-Item -Path $default_app -Force", // Remove default_app.asar which is the sample app

        "Copy-Item -Path $Env:Param_appAsar -Destination $resourceFolder",
    ], {
        environmentVariables: [
            {name: "Param_inElectron", value: extractedElectron },
            {name: "Param_inBrandedExe", value: brandedExe },
            {name: "Param_appAsar", value: appAsar.packFile },
            {name: "Param_output", value: finalOutput },
        ],
        dependencies: [
            extractedElectron,
            brandedExe,
            appAsar.packFile,
        ],
        outputs: [
            finalOutput,
        ],
    });
    const finalFolder = copyHackResult2.getOutputDirectory(finalOutput);

    return {
        appFolder: finalFolder,
    };
}

@@public
export interface Arguments {
    name: string,
    winIcon?: File,
    projectFolder: Directory,
    /** Optional feed to use instead of 'https://registry.yarnpkg.com'. This assumes that restore has to be authenicated as well. */
    authenticatedPackageFeed?: string,
}

@@public
export interface Result {
    appFolder: OpaqueDirectory,
}

