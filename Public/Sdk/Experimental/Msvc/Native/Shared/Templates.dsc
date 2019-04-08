// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Exe           from "Sdk.Native.Tools.Exe";
import * as Dll           from "Sdk.Native.Tools.Dll";
import * as StaticLibrary from "Sdk.Native.Tools.StaticLibrary";

namespace Templates {

    export declare const qualifier : {
        configuration: "debug" | "release",
    };

    const ClQ = Cl.withQualifier({platform: "x86", configuration: qualifier.configuration});
    const LinkQ = Link.withQualifier({platform: "x86", configuration: qualifier.configuration});
    const LibQ = Lib.withQualifier({platform: "x86", configuration: qualifier.configuration});

    /** The default Cl release optimizations */
    @@public
    export const clDebugOptimizations: Cl.Optimizations = {
        optimizationMode: ClQ.OptimizationMode.disabled,
    };

    /** The default Cl release optimizations */
    @@public
    export const clReleaseOptimizations: Cl.Optimizations = {
        optimizationMode: ClQ.OptimizationMode.maximizeSpeed,
        wholeProgram: true,    
    };

    @@public
    export const nativeBuildersLinkRunnerTemplate: Link.Arguments = {
        programDatabaseAlternatePath: "%_PDB%",
        linkTimeCodeGeneration: LinkQ.LinkTimeCodeGenerationOption.use,
        generateDebugInformation: true,
    };

    @@public
    export const nativeBuildersLibRunnerTemplate: Lib.Arguments = {
        inputFormat: LibQ.InputFormat.commonObjectFileFormatAndLTCG,
        treatWarningAsError: true,
    };

    @@public
    export const nativeBuildersClRunnerTemplate: Cl.Arguments = {
        enablePreFast: true,
        treatWarningAsError: true,
        warningLevel: ClQ.ClWarningLevel.enableAllWarnings,
        bufferSecurityCheck: true,
        exceptionHandling: ClQ.ExceptionHandling.sync,
        omitDefaultLibraryName: false,
        debugInformationFormat: ClQ.DebugInformationFormat.oldStyle,
        enableOptimizedDebugInformation: true,
        useFullPaths: true,
        treatWchartAsBuiltInType: false,
        forceConformanceInForLoopScope: true,
        disableSpecificWarnings: [ 4711 ],
        runtimeLibrary: (() => {
            if (qualifier.configuration === "debug") return ClQ.RuntimeLibrary.multithreadedDebug;
            if (qualifier.configuration === "release") return ClQ.RuntimeLibrary.multithreaded;
            return undefined;
        })(),
        preprocessorSymbols: (() => {
            if (qualifier.configuration === "debug") return [ { name: "_DEBUG" } ];
            return undefined;
        })(),
        optimizations: (() => {
            if (qualifier.configuration === "debug") return clDebugOptimizations;
            if (qualifier.configuration === "release") return clReleaseOptimizations;
            return undefined;
        })(),
        sources: undefined,
    };

    @@public
    export interface NativeBinaryInnerTemplates {
        clRunnerTemplate: Cl.Arguments;
        libRunnerTemplate: Lib.Arguments;
        linkRunnerTemplate: Link.Arguments;
    }

    @@public
    export const defaultNativeBinaryInnerTemplates: NativeBinaryInnerTemplates =  {
        clRunnerTemplate: nativeBuildersClRunnerTemplate,
        libRunnerTemplate: nativeBuildersLibRunnerTemplate,
        linkRunnerTemplate: nativeBuildersLinkRunnerTemplate,
    };

    @@public
    export const defaultNativeExeBuilderTemplate: Exe.Arguments = {
        sources: undefined,
        innerTemplates: defaultNativeBinaryInnerTemplates,
        preprocessorSymbols: [
            { name: "_WINDOWS" },
            { name: "_USRDLL" },
            { name: "_WINDLL" },
            { name: "_UNICODE" },
            { name: "UNICODE" },
            {
                name: "VERBOSE",
                value: "0",
            },
        ],
    };

    @@public
    export const defaultNativeDllBuilderTemplate: Dll.Arguments = {
        sources: undefined,
        innerTemplates: defaultNativeBinaryInnerTemplates,
        preprocessorSymbols: [
            { name: "_WINDOWS" },
            { name: "_USRDLL" },
            { name: "_WINDLL" },
            { name: "_UNICODE" },
            { name: "UNICODE" },
            {
                name: "VERBOSE",
                value: "0",
            },
        ],
    };

    @@public
    export const defaultStaticLibraryBuilderTemplate: StaticLibrary.Arguments = {
        sources: undefined,
        innerTemplates: defaultNativeBinaryInnerTemplates,
        preprocessorSymbols: [
            { name: "_WINDOWS" },
            { name: "_USRDLL" },
            { name: "_WINDLL" },
            { name: "_UNICODE" },
            { name: "UNICODE" },
            {
                name: "VERBOSE",
                value: "0",
            },
        ],
    };
}
