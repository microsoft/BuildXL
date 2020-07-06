// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import * as Native from "Sdk.Native";
import {Transformer} from "Sdk.Transformers";

namespace AriaNative {

    export declare const qualifier: {
        configuration: "debug" | "release";
    };

    const needNativeAria = Context.getCurrentHost().os === "win" && BuildXLSdk.Flags.isMicrosoftInternal;
    
    const platform = "x64";
    const AriaPkgContents = importFrom("Aria.Cpp.SDK.win-x64").Contents.all;
    const WindowsSdk = importFrom("WindowsSdk").withQualifier({platform: platform});
    const VisualCpp = importFrom("VisualCpp").withQualifier({platform: platform});
    const native = importFrom("Sdk.Native").withQualifier({platform: platform});

    const ariaWinIncludeDir = Transformer.reSealPartialDirectory(AriaPkgContents, r`win-x64/tools/include`, "win");

    export const clRunnerDefaultValue = native.Templates.nativeBuildersClRunnerTemplate.merge({
        preprocessorSymbols: [],
        treatWarningAsError: false,
        warningLevel: native.Cl.ClWarningLevel.level4,
        disableSpecificWarnings: [
            4668, // not defined as a preprocessor macro, replacing with '0' for '#if/#elif'
            4514, // unreferenced inline function has been removed
        ],
        runtimeTypeInfo: false,
    });
    
    export const innerTemplates = native.Templates.defaultNativeBinaryInnerTemplates.merge({clRunner: clRunnerDefaultValue});

    export const nativeDllBuilderDefaultValue = {
        innerTemplates: innerTemplates,
        platform: platform,
        linkTimeCodeGeneration: qualifier.configuration === "release" ? native.Link.LinkTimeCodeGenerationOption.use : undefined,
    };

    @@public
    export const dll = !needNativeAria ? undefined : native.Dll.build(nativeDllBuilderDefaultValue.merge<Native.Dll.Arguments>({
        outputFileName: a`BuildXLAria.dll`,
        preprocessorSymbols: [
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal,
                {name: "MICROSOFT_INTERNAL"}
            )
        ],

        sources: [
            f`lib/AriaLogger.cpp`
        ],

        includes: [
            f`lib/AriaLogger.hpp`,
            ariaWinIncludeDir,
            WindowsSdk.UM.include,
            WindowsSdk.Shared.include,
            WindowsSdk.Ucrt.include,
            VisualCpp.include,
        ],

        exports: [
            {name: "DllMain"},
            {name: "CreateAriaLogger"},
            {name: "DisposeAriaLogger"},
            {name: "LogEvent"},
        ],

        libraries: [
            AriaPkgContents,
            AriaPkgContents.getFile(r`win-x64/tools/${qualifier.configuration}/ClientTelemetry.lib`),
            VisualCpp.lib,
            WindowsSdk.Ucrt.lib,
            ...WindowsSdk.UM.standardLibs,
        ],
    }));

    @@public
    export const deployment: Deployment.Definition = !needNativeAria ? undefined : {
        contents: [
            {
                subfolder: PathAtom.create(platform),
                contents: [
                    dll.binaryFile,
                    dll.debugFile,
                    ...AriaPkgContents.getFiles([
                        r`win-x64/tools/${qualifier.configuration}/ClientTelemetry.lib`,
                        r`win-x64/tools/${qualifier.configuration}/ClientTelemetry.dll`,
                        // Excluding PDB since it is large, never used, and this deployment gets copied
                        // several times into final deployment/packages
                        // r`win-x64/tools/${qualifier.configuration}/ClientTelemetry.pdb`,
                    ])
                ]
            }
        ],
    };
}
