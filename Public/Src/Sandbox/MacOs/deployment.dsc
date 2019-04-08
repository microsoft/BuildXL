// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as SdkDeployment from "Sdk.Deployment";

namespace Deployment {
    export declare const qualifier: {configuration: "debug" | "release"};

    const pkgPath = d`${importFrom("runtime.osx-x64.BuildXL").Contents.all.root}/runtimes/osx-x64/native`;

    @@public
    export const macBinaryUsage = Context.getCurrentHost().os === "macOS"
        ? "build"
        : BuildXLSdk.Flags.isMicrosoftInternal
            ? "package"
            : "none";

    @@public
    export const kext: SdkDeployment.Definition = {
        contents: [
        {
            subfolder: r`native/MacOS/BuildXLSandbox.kext`,
            contents: macBinaryUsage === "none"
                ? []
                : macBinaryUsage === "package"
                    ? [ SdkDeployment.createFromDisk(d`${pkgPath}/${qualifier.configuration}/BuildXLSandbox.kext`) ]
                    : [{
                        subfolder: a`Contents`,
                        contents: [
                            Sandbox.kext.plist,
                            {
                                subfolder: a`MacOS`,
                                contents: [ Sandbox.kext.sandbox ]
                            },
                            {
                                subfolder: a`Resources`,
                                contents: [ Sandbox.kext.license ]
                            },
                            {
                                subfolder: a`_CodeSignature`,
                                contents: [ Sandbox.kext.codeRes ]
                            }
                        ]
                    }]
        },
        {
            subfolder: r`native/MacOS/BuildXLSandbox.kext.dSYM`,
            contents: macBinaryUsage === "none"
                ? []
                : macBinaryUsage === "package"
                    ? [ SdkDeployment.createFromDisk(d`${pkgPath}/${qualifier.configuration}/BuildXLSandbox.kext.dSYM`) ]
                    : [{
                        subfolder: a`Contents`,
                        contents: [
                            Sandbox.kext.dSYMPlist,
                            {
                                subfolder: r`Resources/DWARF`,
                                contents: [ Sandbox.kext.dSYMDwarf ]
                            }
                        ]
                    }]
        }]
    };

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
        contents: [
            macBinaryUsage === "build"
                ? Sandbox.monitor
                : f`${pkgPath}/${qualifier.configuration}/SandboxMonitor`
        ]
    };

    @@public
    export const ariaLibrary: SdkDeployment.Definition = {
        contents: [
            macBinaryUsage === "build"
                ? Sandbox.libAria
                : f`${pkgPath}/${qualifier.configuration}/libBuildXLAria.dylib`
        ]
    };

    @@public
    export const interopLibrary: SdkDeployment.Definition = {
        contents: [
            macBinaryUsage === "build"
                ? Sandbox.libInterop
                : f`${pkgPath}/${qualifier.configuration}/libBuildXLInterop.dylib`
        ]
    };

    @@public
    export const coreDumpTester: SdkDeployment.Definition = {
        contents: [
            macBinaryUsage === "build"
                ? Sandbox.coreDumpTester
                : f`${pkgPath}/${qualifier.configuration}/CoreDumpTester`
        ]
    };
}
