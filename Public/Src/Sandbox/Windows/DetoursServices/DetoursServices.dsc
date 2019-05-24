// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Native from "Sdk.Native";

namespace Core {
    export declare const qualifier: BuildXLSdk.PlatformDependentQualifier;

    const headers = [
        f`Assertions.h`,
        f`DataTypes.h`,
        f`DetouredFunctions.h`,
        f`DebuggingHelpers.h`,
        f`DetouredFunctionTypes.h`,
        f`DetoursHelpers.h`,
        f`DetoursServices.h`,
        f`CanonicalizedPath.h`,
        f`PolicyResult.h`,
        f`FileAccessHelpers.h`,
        f`globals.h`,
        f`buildXL_mem.h`,
        f`DetouredScope.h`,
        f`SendReport.h`,
        f`StringOperations.h`,
        f`UnicodeConverter.h`,
        f`stdafx.h`,
        f`stdafx-win.h`,
        f`stdafx-mac-common.h`,
        f`stdafx-mac-interop.h`,
        f`stdafx-mac-kext.h`,
        f`targetver.h`,
        f`MetadataOverrides.h`,
        f`HandleOverlay.h`,
        f`PolicySearch.h`,
        f`DeviceMap.h`,
        f`DetouredProcessInjector.h`,
        f`UniqueHandle.h`,
        f`SubstituteProcessExecution.h`,
    ];

    export const pathToDeviceMapLib: PathAtom = a`${qualifier.platform.replace("x", qualifier.configuration)}`;

    const sharedSettings = Detours.Lib.nativeDllBuilderDefaultValue.merge<Native.Dll.Arguments>({
            includes: [
                ...headers,
                importFrom("BuildXL.DeviceMap").Contents.all,
                Detours.Include.includes,
                importFrom("WindowsSdk").UM.include,
                importFrom("WindowsSdk").Shared.include,
                importFrom("WindowsSdk").Ucrt.include,
                importFrom("VisualCpp").include,
            ],
            preprocessorSymbols: [
                {name: "DETOURSSERVICES_EXPORTS"},
                ...addIf(BuildXLSdk.Flags.isMicrosoftInternal,
                    {name: "FEATURE_DEVICE_MAP"}
                ),
            ],
            libraries: [
                Detours.Lib.lib.binaryFile,
                ...importFrom("WindowsSdk").UM.standardLibs,
                ...addIfLazy(BuildXLSdk.Flags.isMicrosoftInternal, () => [
                    importFrom("BuildXL.DeviceMap").Contents.all.getFile(r`${pathToDeviceMapLib}/DeviceMap.lib`),
                ]),
                importFrom("VisualCpp").lib,
                importFrom("WindowsSdk").Ucrt.lib,
            ],
    });

    export const nativesDll: Native.Dll.NativeDllImage = Native.Dll.build(
        sharedSettings.merge<Native.Dll.Arguments>({
            outputFileName: PathAtom.create("BuildXLNatives.dll"),
            preprocessorSymbols: [{name: "BUILDXL_NATIVES_LIBRARY"}],
            sources: [
                f`Assertions.cpp`,
                f`DebuggingHelpers.cpp`,
                f`DetoursServices.cpp`,
                f`DetouredScope.cpp`,
                f`StringOperations.cpp`,
                f`stdafx.cpp`,
                f`MetadataOverrides.cpp`,
                f`PolicySearch.cpp`,
                f`DeviceMap.cpp`,
                f`SendReport.cpp`,
                f`DetouredProcessInjector.cpp`,
                f`SubstituteProcessExecution.cpp`,
            ],

            exports: [
                {name: "DllMain"},
                {name: "IsDetoursDebug"},
                {name: "CreateDetachedProcess"},
                {name: "FindFileAccessPolicyInTree"},
                {name: "NormalizeAndHashPath"},
                {name: "AreBuffersEqual"},
                {name: "RemapDevices"},
                {name: "CreateDetouredProcess"},
                {name: "DetouredProcessInjector_Create"},
                {name: "DetouredProcessInjector_Destroy"},
                {name: "DetouredProcessInjector_Inject"},
            ],
        })
    );

    export const detoursDll: Native.Dll.NativeDllImage = Native.Dll.build(
        sharedSettings.merge<Native.Dll.Arguments>({
            outputFileName: PathAtom.create("DetoursServices.dll"),
            preprocessorSymbols: [{name: "DETOURS_SERVICES_NATIVES_LIBRARY"}],
            sources: [
                f`Assertions.cpp`,
                f`CanonicalizedPath.cpp`,
                f`PolicyResult.cpp`,
                f`PolicyResult_common.cpp`,
                f`DebuggingHelpers.cpp`,
                f`DetoursServices.cpp`,
                f`DetouredFunctions.cpp`,
                f`DetoursHelpers.cpp`,
                f`FileAccessHelpers.cpp`,
                f`DetouredScope.cpp`,
                f`StringOperations.cpp`,
                f`SendReport.cpp`,
                f`stdafx.cpp`,
                f`MetadataOverrides.cpp`,
                f`HandleOverlay.cpp`,
                f`PolicySearch.cpp`,
                f`DeviceMap.cpp`,
                f`DetouredProcessInjector.cpp`,
                f`SubstituteProcessExecution.cpp`,
            ],

            exports: [
                {name: "DllMain"},
                {name: "CreateDetouredProcess"},
                {name: "DetouredProcessInjector_Create"},
                {name: "DetouredProcessInjector_Destroy"},
                {name: "DetouredProcessInjector_Inject"},
            ],
        })
    );
}
