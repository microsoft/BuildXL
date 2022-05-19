// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export declare const qualifier : AllSupportedQualifiers;

/**
 * Qualifier that only supports full targetFramework
 */
@@public
export interface FullFrameworkQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net472";
    targetRuntime: "win-x64"
}

/**
 * Qualifier that only supports .net standard
 */
@@public
export interface NetStandardQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "netstandard2.0" | "netcoreapp3.1";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * Qualifier for projects that support DotNetCore
 */
@@public
export interface DefaultQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "netcoreapp3.1" | "net5.0" | "net6.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * Qualifier for projects that support DotNetCore and .NET472
 */
@@public
export interface DefaultQualifierWithNet472 extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "netcoreapp3.1" | "net5.0" | "net6.0" | "net472";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * Qualifier for projects that support DotNetCore, .NET472 and NetStandard2.0
 */
@@public
export interface DefaultQualifierWithNet472AndNetStandard20 extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "netcoreapp3.1" | "net5.0" | "net6.0" | "net472" | "netstandard2.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * Qualifier for projects that support DotNetCore, .NET472 and NetStandard2.0
 */
@@public
export interface AllSupportedQualifiers extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "netcoreapp3.1" | "net5.0" | "net6.0" | "net472" | "netstandard2.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
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
    targetFramework: "netcoreapp3.1" | "net5.0" | "net6.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

@@public
export interface Net5Qualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net5.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

@@public
export interface Net6Qualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net6.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
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
