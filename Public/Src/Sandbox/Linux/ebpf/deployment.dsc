// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as SdkDeployment from "Sdk.Deployment";

namespace Deployment {
    export declare const qualifier: {configuration: "debug" | "release", targetRuntime: "linux-x64"};

    @@public
    export const natives : SdkDeployment.Definition = Context.getCurrentHost().os === "unix" && {
        contents: [
            // CODESYNC: .azdo/rolling/jobs/linux.yml
            // This environment variable may be set by ADO to provide an alternative deployment for the eBPF binaries built on a different host.
            // This is necessary when building on older kernels that don't support eBPF.
            Environment.hasVariable("BuildXLEbpfSandboxDeploymentOverridePath")
                ? f`${Environment.getPathValue("BuildXLEbpfSandboxDeploymentOverridePath")}/${qualifier.configuration}/${qualifier.targetRuntime}/bxl-ebpf-runner`
                : eBPFSandbox.sandbox
        ]
    };
}