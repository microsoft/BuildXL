// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Native from "Sdk.Linux.Native";

namespace eBPFSandbox {
    export declare const qualifier : {
        configuration: "debug" | "release",
        targetRuntime: "linux-x64"
    };

    // Kernel 6.6+ is required to build the eBPF sandbox.
    // TODO: The inbox dscript SDK has support for this, remove this once the dscript SDK is updated.
    const hostSupportsBuildingEBPF = Context.getCurrentHost().os === "unix"
        && (Environment.hasVariable("MAJOR_KERNEL_VERSION") && Environment.getNumberValue("MAJOR_KERNEL_VERSION") >= 6)
        && (Environment.hasVariable("MINOR_KERNEL_VERSION") && Environment.getNumberValue("MINOR_KERNEL_VERSION") >= 6);

    const bpfTool : Transformer.ToolDefinition = {
        exe: importFrom("bpftool").extracted.assertExistence(r`bpftool`),
        dependsOnCurrentHostOSDirectories: true
    };

    const makeTool : Transformer.ToolDefinition = {
        exe: f`/bin/make`,
        dependsOnCurrentHostOSDirectories: true
    };

    const clangTool : Transformer.ToolDefinition = {
        exe: f`/bin/clang`,
        dependsOnCurrentHostOSDirectories: true
    };

    // CODESYNC: config.dsc
    const libbpfVersion = "libbpf-1.4.7";

    // Headers are always deployed under this well known mount so that VSCode intellisense can pick them up
    const headerDeploymentDirectory : Directory = d`${Context.getMount("ebpfheaders").path}/${qualifier.configuration}`;

    // Build libbpf
    // libbpf binaries can be found at `objDir/libbpf.so`, `objDir\libbpf.pc`, `objDir\libbpf.a`
    // Objects can be found under `objDir/sharedObjs` or `objDir/staticobjs` for shared or statically linked objects
    const objDir : Directory = Context.getNewOutputDirectory("obj");
    const destDir : Directory = Context.getNewOutputDirectory("dest");
    const workingDirectory : Directory = d`${importFrom("libbpf").extracted.path}/${libbpfVersion}/src`;

    // Calling make here instead of directly calling gcc/gxx to avoid needing to make changes when
    // libbpf changes the makefile.
    const libbpf : Transformer.ExecuteResult = Transformer.execute({
        tool:                       makeTool,
        arguments:                  [],
        workingDirectory:           workingDirectory,
        dependencies:               [ importFrom("libbpf").extracted ],
        environmentVariables:       [
                                        { name: "OBJDIR", value: objDir },
                                        { name: "DESTDIR", value: destDir },
                                    ],
        outputs:                    [
                                        { kind: "exclusive", directory: d`${objDir.path}` },
                                        { kind: "exclusive", directory: d`${destDir.path}` },
                                    ],
        tempDirectory:              Context.getTempDirectory("temp"),
        unsafe: {
            untrackedScopes: [
                d`/lib32`,
                d`/libx32`
            ]
        }
    });

    // Deploy libbpf headers
    // `make install` for libbpf will deploy these under /usr/include
    const bpfheaders : File[] = [
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/bpf.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/libbpf.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/btf.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/libbpf_common.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/libbpf_legacy.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/bpf_helpers.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/bpf_helper_defs.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/bpf_tracing.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/bpf_endian.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/bpf_core_read.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/skel_internal.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/libbpf_version.h`),
        importFrom("libbpf").extracted.assertExistence(r`${libbpfVersion}/src/usdt.bpf.h`),
    ];

    const deployedHeaders = [
        ...bpfheaders.map(element => Transformer.copyFile(element, p`${headerDeploymentDirectory.path}/bpf/${element.name}`)),
    ];

    // Build Sandbox
    // Build BPF code
    const bpfCodeClangOutputDir : Directory = Context.getNewOutputDirectory("tmp");
    const bpfCodeInputFile : File = f`sandbox.bpf.c`;
    const bpfCodeTmpOutputPath : Path = p`${bpfCodeClangOutputDir.path}/sandbox.tmp.bpf.o`;
    const bpfCodeClang : Transformer.ExecuteResult = Transformer.execute({
        tool:                       clangTool,
        arguments:                  [
                                        Cmd.rawArgument("-g"),
                                        Cmd.option("-O", "2"),
                                        Cmd.option("-target ", "bpf"),
                                        Cmd.rawArgument("-Werror"),
                                        Cmd.rawArgument("-D__TARGET_ARCH_x86"),
                                        Cmd.options("-I", [
                                            d`.`,
                                            d`vmlinux`,
                                            d`${importFrom("libbpf").extracted.path}/${libbpfVersion}/include/uapi`,
                                            headerDeploymentDirectory
                                        ].map(Artifact.none)),
                                        Cmd.option("-c ", Artifact.input(bpfCodeInputFile)),
                                        Cmd.option("-o ", Artifact.output(bpfCodeTmpOutputPath)),
                                    ],
        workingDirectory:           workingDirectory,
        dependencies:               [ 
                                        bpfCodeInputFile,
                                        ...globR(d`.`, "*.h"),
                                        ...glob(d`${Context.getSpecFileDirectory().parent}`, "*.h"),
                                        importFrom("libbpf").extracted,
                                        ...deployedHeaders
                                    ],
        environmentVariables:       [],
        outputs:                    [
                                        { kind: "exclusive", directory: d`${bpfCodeClangOutputDir.path}` },
                                    ],
        tempDirectory:              Context.getTempDirectory("temp")
    });

    const bpfCodeOutputDir : Directory = Context.getNewOutputDirectory("bpf");
    const bpfCodeTmpOutputFile : DerivedFile = bpfCodeClang.getOutputFile(bpfCodeTmpOutputPath);
    const bpfObjectPath : Path = p`${bpfCodeOutputDir.path}/sandbox.bpf.o`;

    const bpfCode : Transformer.ExecuteResult = Transformer.execute({
        tool:                       bpfTool,
        arguments:                  [
                                        Cmd.rawArgument("gen"),
                                        Cmd.rawArgument("object"),
                                        Cmd.argument(Artifact.output(bpfObjectPath)),
                                        Cmd.argument(Artifact.input(bpfCodeTmpOutputFile)),
                                    ],
        dependencies:               [...bpfCodeClang.getOutputDirectories(), ...bpfCodeClang.getOutputFiles()],
        workingDirectory:           bpfCodeOutputDir,
    });
    
    // Build BPF skeleton
    const bpfObjectFile : DerivedFile = bpfCode.getOutputFile(bpfObjectPath);
    const bpfskelPath : Path = p`${headerDeploymentDirectory.path}/sandbox.skel.h`;
    const bpfskel : Transformer.ExecuteResult = Transformer.execute({
        tool:                       bpfTool,
        arguments:                  [
                                        Cmd.rawArgument("gen"),
                                        Cmd.rawArgument("skeleton"),
                                        Cmd.argument(Artifact.input(bpfObjectFile)),
                                    ],
        dependencies:               [...bpfCode.getOutputFiles()],
        workingDirectory:           headerDeploymentDirectory,
        consoleOutput:              bpfskelPath
    });

    // Build userspace code
    const sandboxSource : File[] = [
        f`sandbox.cpp`,
        f`SyscallHandler.cpp`,
        f`../bxl_observer.cpp`,
        f`../observer_utilities.cpp`,
        f`../ReportBuilder.cpp`,
        f`../SandboxEvent.cpp`,
        f`../AccessChecker.cpp`
    ];

    const commonSrc = [
        f`../../Windows/DetoursServices/PolicyResult_common.cpp`,
        f`../../Windows/DetoursServices/PolicySearch.cpp`,
        f`../../Windows/DetoursServices/StringOperations.cpp`,
        f`../../Windows/DetoursServices/FilesCheckedForAccess.cpp`,
        f`../../Common/FileAccessManifest.cpp`
    ];

    const utilsSrc   = [ f`../utils.c` ];

    const includeDirectories = [
        headerDeploymentDirectory,
        d`./`,
        d`vmlinux`,
        d`../`,
        d`../../Windows/DetoursServices`,
        d`../../Common`,
        d`${importFrom("libbpf").extracted.path}/${libbpfVersion}/include/uapi`,
    ];

    const userHeaders = [
        d`./`,    
        d`../`,
        d`../../Windows/DetoursServices`,
        d`../../Common`,
    ].mapMany(d => ["*.h", "*.hpp"].mapMany(q => glob(d, q)));

    function compile(sourceFile: SourceFile) : DerivedFile {
        const compilerArgs : Native.Linux.Compilers.CompilerArguments =  {
            defines: [],
            headers: userHeaders,
            additionalDependencies: [
                importFrom("libbpf").extracted,
                ...deployedHeaders,
                ...bpfskel.getOutputFiles(),
            ],
            includeDirectories: includeDirectories,
            sourceFile: sourceFile
        };

        return Native.Linux.Compilers.compile(compilerArgs);
    }

    const sandboxObj = sandboxSource.map(compile);
    const commonObj = commonSrc.map(compile);
    const utilsObj = utilsSrc.map(compile);
    const libbpfa : File = libbpf.getOutputDirectory(objDir).assertExistence(r`libbpf.a`);

    // Build final sandbox binary
    // CODESYNC Public/Src/Engine/Processes/SandboxConnectionLinuxEBPF.cs
    @@public
    export const sandbox = hostSupportsBuildingEBPF
    ?
        Native.Linux.Compilers.link({
            outputName: a`bxl-ebpf-runner`,
            tool: Native.Linux.Compilers.gxxTool,
            objectFiles: [...utilsObj, ...commonObj, ...sandboxObj, libbpfa],
            libraries: [ "rt", "dl", "pthread", "m", "elf", "z" ]
        })
    : undefined;
}