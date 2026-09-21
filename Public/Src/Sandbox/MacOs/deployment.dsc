// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as SdkDeployment from "Sdk.Deployment";

namespace Deployment {
    export declare const qualifier: {configuration: "debug" | "release", targetRuntime: "osx-x64" | "osx-arm64"};

    /**
     * The macOS native interop library.
     *
     * libBuildXLInterop.dylib is P/Invoked by BuildXL.Interop.MacOS (see
     * Public/Src/Utilities/Utilities.Core/Interop/MacOS/Impl.Mac.cs) for core file system, memory
     * and process operations, so it must be deployed next to bxl for BuildXL to run on macOS.
     *
     * The dylib is built with Xcode on a macOS agent and published as a NuGet package
     * (see .azdo/rolling/jobs/mac.yml and Private/macOS/prepare-macos-runtime-package.sh).
     * Consuming the prebuilt package instead of building from source lets the macOS deployment be
     * produced by a cross-compiling build on any host OS.
     *
     * The package contains a universal dylib that supports both x86-64 and arm64 macOS hosts.
     */
    @@public
    export const natives : SdkDeployment.Definition = {
        contents: [
            importFrom("Microsoft.BuildXL.Interop.Runtime.osx-x64").Contents.all.getFile(
                r`runtimes/osx/native/libBuildXLInterop.dylib`)
        ]
    };
}
