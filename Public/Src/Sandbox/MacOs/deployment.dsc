// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as SdkDeployment from "Sdk.Deployment";

namespace Deployment {
    export declare const qualifier: {configuration: "debug" | "release"};

    @@public
    export const macBinaryUsage = Context.getCurrentHost().os === "macOS"
        ? (BuildXLSdk.Flags.isValidatingOsxRuntime ? "package" : "build")
        : (BuildXLSdk.Flags.isMicrosoftInternal    ? "package" : "none");

    const EnvScript = f`${Context.getMount("Sandbox").path}/MacOs/scripts/env.sh`;

    @@public
    export const buildXLScripts: SdkDeployment.Definition = {
        contents: [
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/bxl.sh`,
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/bxl.sh.1`,
            EnvScript
        ]
    };

    @@public
    export const sandboxLoadScripts: SdkDeployment.Definition = {
        contents: [
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/sandbox-load.sh`,
            f`${Context.getMount("Sandbox").path}/MacOs/scripts/sandbox-load.sh.1`,
            EnvScript
        ]
    };

    @@public
    export const sandboxMonitor: SdkDeployment.Definition = {
        contents: 
            macBinaryUsage === "build"
                ? [Sandbox.monitor]
                : macBinaryUsage === "package" 
                    ? [importFrom("runtime.osx-x64.BuildXL").Contents.all.getFile(r`runtimes/osx-x64/native/${qualifier.configuration}/SandboxMonitor`)]
                    : []
    };

    @@public
    export const interopLibrary: SdkDeployment.Definition = {
        contents: macBinaryUsage === "build"
            ? [ Sandbox.libInterop, Sandbox.libDetours ]
            : macBinaryUsage === "package" 
                ? [
                    importFrom("runtime.osx-x64.BuildXL").Contents.all.getFile(r`runtimes/osx-x64/native/${qualifier.configuration}/libBuildXLInterop.dylib`),
                    importFrom("runtime.osx-x64.BuildXL").Contents.all.getFile(r`runtimes/osx-x64/native/${qualifier.configuration}/libBuildXLDetours.dylib`)
                ]
                : []
    };

    @@public
    export const coreDumpTester: SdkDeployment.Definition = {
        contents: 
            macBinaryUsage === "build"
                ? [Sandbox.coreDumpTester]
                : macBinaryUsage === "package" 
                    ? [importFrom("runtime.osx-x64.BuildXL").Contents.all.getFile(r`runtimes/osx-x64/native/${qualifier.configuration}/CoreDumpTester`)]
                    : []
    };

    @@public
    export const bxlESDaemon: SdkDeployment.Definition = {
        contents: [{
            subfolder: r`native/MacOS/`,
            contents: Sandbox.bxlESDaemon
        }]
    };
}
