// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Deployment from "Sdk.Deployment";

@@public
export interface Framework {
    /**
     * The minimum runtime version supported.
     * See: https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/startup/supportedruntime-element
     */
    supportedRuntimeVersion: string,

    /** The qualifier targetFramework moniker */
    targetFramework: string;

    /** The targetFramework name to be placed in TargetFrameworkAttribute in AssemblyInfo file */
    assemblyInfoTargetFramework: string;

    /** The frameworkDisplayName name to be placed in TargetFrameworkAttribute in AssemblyInfo file */
    assemblyInfoFrameworkDisplayName: string;

    /** The standard references that should be added to the compile list */
    standardReferences: Reference[];

    /** Whether to generate portablePdb */
    requiresPortablePdb: boolean;

    /** The style of runtime configuration files */
    runtimeConfigStyle: RuntimeConfigStyle;

    /** Optional set of defines to be passed to compilers to indicate the current target framework */
    conditionalCompileDefines: string[];

    /** When the runtimeConfigStyle is runtimeJson, a runtime framework name is required */
    runtimeFrameworkName?: string;

    /** When the runtimeConfigStyle is runtimeJson, a version is required */
    runtimeConfigVersion?: string;

    /** Whether applications should be deployed framework-dependent or self-contained */
    defaultApplicationDeploymentStyle?: ApplicationDeploymentStyle;

    /** When ApplicationDeploymentStyle is selfContained, runtime files have to be provided for the application execution environment */
    runtimeContentProvider?: (version: RuntimeVersion) => File[];

    /** Framework-specific files for crossgen tool. Only available for netcore app frameworks.*/
    crossgenProvider?: (version: RuntimeVersion) => CrossgenFiles;

}

/** Whether the given framework supports crossgen */
@@public 
export function supportsCrossgen(deploymentStyle: ApplicationDeploymentStyle, framework: Framework): boolean {
    // crossgen is supported when the underlying framework sets a provider for it and the application deployment style is
    // self-contained
    return deploymentStyle === "selfContained" && framework.crossgenProvider !== undefined;
}


/** Path to crossgen tool and its corresponding JIT compiler. */
@@public
export interface CrossgenFiles {
    crossgenExe: File;
    JITPath: File;
}

export type RuntimeConfigStyle = "appConfig" | "runtimeJson" | "none";

@@public
export type ApplicationDeploymentStyle = "frameworkDependent" | "selfContained";

@@public
export type RuntimeVersion = "win-x64" | "osx-x64" | "linux-x64";

@@public
export type DotNetCoreVersion = "net6.0";

@@public
export function isDotNetCore(targetFramework: TargetFrameworks.AllFrameworks) : targetFramework is DotNetCoreVersion {
    return targetFramework === 'net6.0';
}

namespace TargetFrameworks {
    @@public
    export const DefaultTargetFramework = "net6.0";
    
    @@public
    export type DesktopTargetFrameworks = "net472";

    @@public
    export type CoreClrTargetFrameworks = "net6.0";

    @@public
    export type StandardTargetFrameworks = "netstandard2.0";

    @@public
    export type AllFrameworks = DesktopTargetFrameworks | CoreClrTargetFrameworks | StandardTargetFrameworks;

    @@public
    export interface ConfigurationQualifier extends Qualifier {
        configuration: "debug" | "release"
    }

    @@public
    export interface BaseQualifier extends ConfigurationQualifier, Qualifier {
        targetRuntime: RuntimeVersion;
    }

    @@public
    export interface Desktop extends BaseQualifier, Qualifier {
        targetFramework: DesktopTargetFrameworks;
    }

    @@public
    export interface CoreClr extends BaseQualifier, Qualifier {
        targetFramework: CoreClrTargetFrameworks;
    }

    @@public
    export interface All extends BaseQualifier, Qualifier {
        targetFramework: AllFrameworks;
    }

    /** Current Machine qualifier that respect current configuration */
    namespace MachineQualifier {
        export declare const qualifier: {configuration: "debug" | "release" };

        @@public
        export interface Current extends Qualifier {
            configuration: "debug" | "release";
            targetFramework: "net6.0",
            targetRuntime: "win-x64" | "osx-x64" | "linux-x64",
        }

        @@public
        export interface CurrentWithStandard extends Qualifier {
            configuration: "debug" | "release";
            targetFramework: "net6.0" | "netstandard2.0",
            targetRuntime: "win-x64" | "osx-x64" | "linux-x64",
        }

        @@public
        export const current : Current = {
            configuration: qualifier.configuration,
            targetFramework: "net6.0",
            targetRuntime: 
                Context.getCurrentHost().os === "win"   ? "win-x64" : 
                Context.getCurrentHost().os === "macOS" ? "osx-x64" :
                "linux-x64",
        };
    }
}
