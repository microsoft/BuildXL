// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as MacServices from "BuildXL.Sandbox.MacOS";

namespace Tools {

    export declare const qualifier : { configuration: "debug" | "release"};

    namespace Helpers {
        export declare const qualifier: BuildXLSdk.DefaultQualifier;

        export function getTargetLocation(toolName: string) : RelativePath {
            return r`${qualifier.targetFramework}/${qualifier.configuration}/tools/${toolName}/${qualifier.targetRuntime}`;
        }
    }
    
    namespace AdoBuildRunner {

        export declare const qualifier: BuildXLSdk.DefaultQualifier;

        export const deployment : Deployment.Definition = {
            contents: [
                importFrom("BuildXL.AdoBuildRunner").withQualifier({
                    targetFramework: qualifier.targetFramework,
                }).BuildXL.AdoBuildRunner.exe
            ],
        };

        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: Helpers.getTargetLocation("AdoBuildRunner"),
        });
    }

    namespace DistributedBuildRunner {
        export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

        export const deployment : Deployment.Definition = {
            contents: [
                importFrom("BuildXL.Tools").DistributedBuildRunner.exe,
            ],
        };

        const frameworkSpecificPart = BuildXLSdk.isDotNetCoreOrStandard
            ? qualifier.targetRuntime
            : qualifier.targetFramework;

        const deployed = BuildXLSdk.DeploymentHelpers.deploy({
            definition: deployment,
            targetLocation: (qualifier.targetFramework === Managed.TargetFrameworks.DefaultTargetFramework) // If targetFramework is not a default one (net8.0), then we put it in a separate directory.
            ? r`${qualifier.configuration}/tools/DistributedBuildRunner/${frameworkSpecificPart}` 
            : r`${qualifier.targetFramework}/${qualifier.configuration}/tools/DistributedBuildRunner/${qualifier.targetRuntime}`,
            omitFromDrop: true,
        });
    }
}
