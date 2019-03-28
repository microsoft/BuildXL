// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export declare const qualifier : AllSupportedQualifiers;

/**
 * Qualifier that only supports full targetFramework
 */
@@public
export interface FullFrameworkQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net461" | "net472";
    targetRuntime: "win-x64"
}

/**
 * Qualifier for projects that support DotNetCore
 */
@@public
export interface DefaultQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net461" | "net472" | "netcoreapp2.2";
    targetRuntime: "win-x64" | "osx-x64";
}

/**
 * Qualifier for projects that support DotNetCore
 */
@@public
export interface DefaultQualifierWithNet451 extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net451" | "net461" | "net472" | "netcoreapp2.2";
    targetRuntime: "win-x64" | "osx-x64";
}

/**
 * Qualifier for projects that support DotNetCore
 */
@@public
export interface DefaultQualifierWithNet451AndNetStandard20 extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net451" | "net461" | "net472" | "netcoreapp2.2" | "netstandard2.0";
    targetRuntime: "win-x64" | "osx-x64";
}

/**
 * This qualifier should become the default when BuildXL gets ported to Net472
 * In the meantime, it is only used by some cherry-picked projects
 */
@@public
export interface DefaultQualifierWithNet451AndNet472 extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net451" | "net461" | "net472" | "netcoreapp2.2";
    targetRuntime: "win-x64" | "osx-x64";
}


export interface AllSupportedQualifiers extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net451" | "net461" | "net472" | "netcoreapp2.2" | "netstandard2.0";
    targetRuntime: "win-x64" | "osx-x64";
}

/**
 * Extension of the default qualifier with a platform
 */
@@public
export interface PlatformDependentQualifier extends Qualifier {
    platform: "x86" | "x64";
    configuration: "debug" | "release";
}

@@public
export interface NetCoreAppQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "netcoreapp2.2";
    targetRuntime: "win-x64" | "osx-x64";
}

/**
 * LatestFullFrameworkQualifier, used to pin tool execution to a specific .NET Framework version
 */
@@public
export const LatestFullFrameworkQualifier : FullFrameworkQualifier = {
    configuration: qualifier.configuration,
    targetFramework: "net472",
    targetRuntime: "win-x64"
};

/**
 * Converst the qualifier to the latest supported qualifier.
 */
@@public
export const LatestQualifier : DefaultQualifier = {
    configuration: qualifier.configuration,
    targetFramework: isDotNetCoreBuild ? "netcoreapp2.2" : "net461",
    targetRuntime: qualifier.targetRuntime
};


/**
 * Converst the qualifier to the latest supported qualifier.
 */
@@public
export const Net451Qualifier : DefaultQualifierWithNet451 = {
    configuration: qualifier.configuration,
    targetFramework: "net451",
    targetRuntime: qualifier.targetRuntime
};
