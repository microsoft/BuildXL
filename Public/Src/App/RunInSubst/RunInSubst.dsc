// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace RunInSubst {
    export declare const qualifier: BuildXLSdk.PlatformDependentQualifier;

    const headers = [f`stdafx.h`];

    @@public
    export const preprocessorSymbols = {
        items: [
            "RUNINSUBST_X86_X64",
            "_MBCS",
            "_UNICODE",
            "UNICODE",
            "RUNINSUBST_X86",
            "RUNINSUBST_32BIT",
        ],
    };

    @@public
    export const nativeInnerTemplate = Native.Templates.defaultNativeBinaryInnerTemplates.merge({
        clRunner: Native.Templates.nativeBuildersClRunnerTemplate.merge({
            preprocessorSymbols: preprocessorSymbols.items,
            disableSpecificWarnings: [4505],
            runtimeTypeInfo: false,
        })
    });

    @@public
    export const staticLibraryBuilderDefaultValue = {
        innerTemplates: nativeInnerTemplate,
        platform: qualifier.platform,
        linkTimeCodeGeneration: qualifier.configuration === "release"
            ? Native.Link.LinkTimeCodeGenerationOption.use
            : undefined,
    };

    @@public
    export const nativeExeBuilderDefaultValue = {
        innerTemplates: nativeInnerTemplate,
        platform: qualifier.platform,
        linkTimeCodeGeneration: qualifier.configuration === "release"
            ? Native.Link.LinkTimeCodeGenerationOption.use
            : undefined,
    };

    @@public
    export const exe: Native.Exe.NativeExeImage = Native.Exe.build(
        nativeExeBuilderDefaultValue.merge<Native.Exe.Arguments>({
            outputFileName: PathAtom.create("RunInSubst.exe"),
            sources: [f`RunInSubst.cpp`],
            includes: [
                ...headers,
                importFrom("WindowsSdk").UM.include,
                importFrom("WindowsSdk").Shared.include,
                importFrom("WindowsSdk").Ucrt.include,
                importFrom("VisualCpp").include,
            ],
            preprocessorSymbols: [
                {name: "RUNINSUBST_EXPORTS"},
            ],
            libraries: [
                ...importFrom("WindowsSdk").UM.standardLibs,
                importFrom("VisualCpp").lib,
                importFrom("WindowsSdk").Ucrt.lib,
            ],
        })
    );

    // Used to deploy the x86 of RunInsubst next to BuildXL.
    // x86 should be fine since it is runnable on x64 machine too.
    @@public
    export const deployment: Deployment.Definition = {
        contents: [
            exe.binaryFile, 
            exe.debugFile
        ]
    };
}
