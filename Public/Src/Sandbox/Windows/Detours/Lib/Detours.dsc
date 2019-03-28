// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Detours.Lib {
    
    export declare const qualifier: BuildXLSdk.PlatformDependentQualifier;
    
    export const preprocessorSymbols = {
        items: [
            "DETOURS_X86_X64",
            "_MBCS",
            ...qualifier.platform === "x64" ? ["DETOURS_X64", "DETOURS_64BIT"] : (qualifier.platform !== "x64" ? ["DETOURS_X86", "DETOURS_32BIT"] : undefined),
        ],
    };
    
    export const clRunnerDefaultValue = Native.Templates.nativeBuildersClRunnerTemplate.merge({
        preprocessorSymbols: [...preprocessorSymbols.items],
        disableSpecificWarnings: [4505, 4702, 4722],
        runtimeTypeInfo: false,
    });
    
    export const detoursNativeBinaryInnerTemplates = Native.Templates.defaultNativeBinaryInnerTemplates.merge({clRunner: clRunnerDefaultValue});
    
    export const staticLibraryBuilderDefaultValue = {
        innerTemplates: detoursNativeBinaryInnerTemplates,
        platform: qualifier.platform,
        linkTimeCodeGeneration: qualifier.configuration === "release" ? Native.Link.LinkTimeCodeGenerationOption.use : undefined,
    };
    
    export const nativeDllBuilderDefaultValue = {
        innerTemplates: detoursNativeBinaryInnerTemplates,
        platform: qualifier.platform,
        linkTimeCodeGeneration: qualifier.configuration === "release" ? Native.Link.LinkTimeCodeGenerationOption.use : undefined,
    };
    
    export const nativeExeBuilderDefaultValue = {
        innerTemplates: detoursNativeBinaryInnerTemplates,
        platform: qualifier.platform,
        linkTimeCodeGeneration: qualifier.configuration === "release" ? Native.Link.LinkTimeCodeGenerationOption.use : undefined,
    };
    
    export const clRunnerForDetoursLib = clRunnerDefaultValue.merge({warningLevel: Native.Cl.ClWarningLevel.level4});
    
    export const detoursNativeBinaryInnerTemplatesForLib = detoursNativeBinaryInnerTemplates.merge({clRunner: clRunnerForDetoursLib});
    
    export const lib = Native.StaticLibrary.build(
        staticLibraryBuilderDefaultValue.merge<Native.StaticLibrary.Arguments>({
            innerTemplates: detoursNativeBinaryInnerTemplatesForLib,
            outputFileName: a`Detours.lib`,
            sources: [
                f`detours.cpp`,
                f`creatwth.cpp`,
                f`disasm.cpp`,
                f`image.cpp`,
                f`modules.cpp`,
                f`Detours.ds`,
                f`tracing.cpp`,
            ],
            includes: [
                f`uimports.cpp`,
                f`tracing.h`,
                f`target.h`,
                Detours.Include.includes,
                importFrom("WindowsSdk").UM.include,
                importFrom("WindowsSdk").Shared.include,
                importFrom("WindowsSdk").Ucrt.include,
                importFrom("VisualCpp").include,
            ],
        })
    );
}
