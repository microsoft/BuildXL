// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Native from "Sdk.Linux.Native";
import * as EBPF from "BuildXL.Sandbox.Linux.eBPF";

namespace Test.eBPFSandbox {
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };

    function compile(sourceFile: SourceFile) : DerivedFile {
        const compilerArgs : Native.Linux.Compilers.CompilerArguments =  {
            defines: [],
            headers: EBPF.eBPFSandbox.userHeaders,
            additionalDependencies: [
                importFrom("libbpf").extracted,
                ...EBPF.eBPFSandbox.deployedHeaders,
                ...EBPF.eBPFSandbox.bpfskel.getOutputFiles(),
                f`test_utils.hpp`
            ],
            includeDirectories: EBPF.eBPFSandbox.includeDirectories,
            sourceFile: sourceFile
        };

        return Native.Linux.Compilers.compile(compilerArgs);
    }

    const ringbufferTestSources = [
        f`ringbuffer_test.cpp`,
        f`../../ebpf/SyscallHandler.cpp`,
        f`../../ebpf/EventRingBuffer.cpp`,
        f`../../bxl_observer.cpp`,
        f`../../observer_utilities.cpp`,
        f`../../ReportBuilder.cpp`,
        f`../../SandboxEvent.cpp`,
        f`../../AccessChecker.cpp`
    ];

    const ringBufferTestObj = ringbufferTestSources.map(compile);
    const commonObj = EBPF.eBPFSandbox.commonSrc.map(compile);
    const utilsObj = EBPF.eBPFSandbox.utilsSrc.map(compile);

    @@public
    export const ringbufferTest = EBPF.eBPFSandbox.hostSupportsBuildingEBPF
    ?
        Native.Linux.Compilers.link({
            outputName: a`ringbuffer_test`,
            tool: Native.Linux.Compilers.gxxTool,
            objectFiles: [...utilsObj, ...commonObj, ...ringBufferTestObj, EBPF.eBPFSandbox.libbpfa],
            libraries: [ "rt", "dl", "pthread", "m", "elf", "z", "numa" ]
        })
    : undefined;

    const incrementalPathTest = [
        f`incremental_path_test.cpp`,
    ];

    const sendProbeObj = incrementalPathTest.map(compile);

    @@public
    export const sendProbe = EBPF.eBPFSandbox.hostSupportsBuildingEBPF
    ?
        Native.Linux.Compilers.link({
            outputName: a`incremental_path_test`,
            tool: Native.Linux.Compilers.gxxTool,
            objectFiles: [...utilsObj, ...sendProbeObj, EBPF.eBPFSandbox.libbpfa],
            libraries: [ "rt", "dl", "pthread", "m", "elf", "z", "numa" ]
        })
    : undefined;

    const pathCanonicalizationTest = [
        f`path_canonicalization_test.cpp`,
    ];

    const pathCanonicalizationObj = pathCanonicalizationTest.map(compile);

    @@public
    export const pathCanonicalization = EBPF.eBPFSandbox.hostSupportsBuildingEBPF
    ?
        Native.Linux.Compilers.link({
            outputName: a`path_canonicalization_test`,
            tool: Native.Linux.Compilers.gxxTool,
            objectFiles: [...utilsObj, ...pathCanonicalizationObj, EBPF.eBPFSandbox.libbpfa],
            libraries: [ "rt", "dl", "pthread", "m", "elf", "z", "numa" ]
        })
    : undefined;

    const pidNamespaceTestSources = [
        f`pid_namespace_test.cpp`,
    ];

    const pidNamespaceTestObj = pidNamespaceTestSources.map(compile);

    @@public
    export const pidNamespaceTest = EBPF.eBPFSandbox.hostSupportsBuildingEBPF
        ?
            Native.Linux.Compilers.link({
                outputName: a`pid_namespace_test`,
                tool: Native.Linux.Compilers.gxxTool,
                objectFiles: [...pidNamespaceTestObj],
                libraries: [ "pthread" ]
            })
        : undefined;
}