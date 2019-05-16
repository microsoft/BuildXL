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

    /** When the runtimeConfigStyle is runtimeJson, a runtime framework name is required */
    runtimeFrameworkName?: string;

    /** When the runtimeConfigStyle is runtimeJson, a version is required */
    runtimeConfigVersion?: string;

    /** Whether applications should be deployed framework-dependent or self-contained */
    applicationDeploymentStyle?: ApplicationDeploymentStyle;

    /** When ApplicationDeploymentStyle is selfContained, runtime files have to be provided for the application execution environment */
    runtimeContentProvider?: (version: RuntimeVersion) => File[];
}

export type RuntimeConfigStyle = "appConfig" | "runtimeJson" | "none";

@@public
export type ApplicationDeploymentStyle = "frameworkDependent" | "selfContained";

@@public
export type RuntimeVersion = "win-x64" | "osx-x64" | "linux-x64";

namespace TargetFrameworks {
    @@public
    export type DesktopTargetFrameworks = "net451" | "net461" | "net472";

    @@public
    export type CoreClrTargetFrameworks = "netcoreapp2.2" | "netcoreapp3.0";

    @@public
    export type StandardTargetFrameworks = "netstandard2.0";

    @@public
    export interface BaseQualifier {
        configuration: "debug" | "release";
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
        targetFramework: DesktopTargetFrameworks | CoreClrTargetFrameworks | StandardTargetFrameworks;
    }

    @@public
    export interface CurrentMachineQualifier extends Qualifier {
        configuration: "debug" | "release";
        // TODO: Netstandard should handle its application deploy in the framework itself and not rely on BuildXLSdk specifics
        targetFramework: "net472" | "netcoreapp3.0",
        targetRuntime: "win-x64" | "osx-x64",
    }

    @@public
    export const currentMachineQualifier : CurrentMachineQualifier = {
        configuration: "release",
        targetFramework: Context.getCurrentHost().os === "win" ? "net472" : "netcoreapp3.0",
        targetRuntime: Context.getCurrentHost().os === "win" ? "win-x64" : "osx-x64",
    };
}
