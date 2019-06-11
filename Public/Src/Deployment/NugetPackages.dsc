// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Branding from "BuildXL.Branding";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release" };

    const canBuildAllPackagesOnThisHost = Context.getCurrentHost().os === "win";

    const packageNamePrefix = BuildXLSdk.Flags.isMicrosoftInternal
        ? "BuildXL"
        : "Microsoft.BuildXL";

    const packageTargetFolder = BuildXLSdk.Flags.isMicrosoftInternal
        ? r`${qualifier.configuration}/pkgs`
        : r`${qualifier.configuration}/public/pkgs`;

    const net472 = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.net472`,
        deployment: BuildXL.withQualifier({
            configuration: qualifier.configuration,
            targetFramework: "net472",
            targetRuntime: "win-x64"
        }).deployment,
    });

    const winX64 = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.win-x64`,
        deployment: BuildXL.withQualifier({
            configuration: qualifier.configuration,
            targetFramework: "netcoreapp3.0",
            targetRuntime: "win-x64"
        }).deployment,
    });

    const osxX64 = pack({
        id: `${packageNamePrefix}.osx-x64`,
        deployment: BuildXL.withQualifier({
            configuration: qualifier.configuration,
            targetFramework: "netcoreapp3.0",
            targetRuntime: "osx-x64"
        }).deployment,
    });

    const sdks = pack({
        id: `${packageNamePrefix}.Sdks`,
        deployment: Sdks.deployment,
    });

    const cacheTools = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.Cache.Tools`,
        deployment: Cache.NugetPackages.tools,
    });

    const cacheLibraries = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.Cache.Libraries`,
        deployment: Cache.NugetPackages.libraries,
        dependencies: [
            { id: `${packageNamePrefix}.Cache.Interfaces`, version: Branding.Nuget.packageVersion},

            importFrom("Microsoft.Tpl.Dataflow").withQualifier({targetFramework: "net461"}).pkg,
            importFrom("System.Interactive.Async").withQualifier({targetFramework: "net461"}).pkg,
            importFrom("Grpc.Core").withQualifier({ targetFramework: "net461" }).pkg,
            importFrom("Google.Protobuf").withQualifier({ targetFramework: "net461" }).pkg,
            importFrom("StackExchange.Redis.StrongName").withQualifier({ targetFramework: "net461" }).pkg,

            ...BuildXLSdk.withQualifier({
                targetFramework: "net461",
                targetRuntime: "win-x64",
                configuration: qualifier.configuration
            }).visualStudioServicesArtifactServicesSharedPkg,

            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").withQualifier({ targetFramework: "net461" }).pkg,
        ]
    });

    const cacheInterfaces = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.Cache.Interfaces`,
        deployment: Cache.NugetPackages.interfaces,
        dependencies: [
            importFrom("Microsoft.Tpl.Dataflow").withQualifier({targetFramework: "net461"}).pkg,
            importFrom("System.Interactive.Async").withQualifier({targetFramework: "net461"}).pkg,
        ]
    });

    const cacheHashing = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.Cache.Hashing`,
        deployment: Cache.NugetPackages.hashing
    });


    // Currently we deploy tools as self-contained .NET Core binaries for macOS only!
    const toolsSandBoxExec = pack({
        id: `${packageNamePrefix}.Tools.SandboxExec.osx-x64`,
        deployment: Tools.SandboxExec.withQualifier({
            configuration: qualifier.configuration,
            targetFramework: "netcoreapp3.0",
            targetRuntime: "osx-x64"
        }).deployment
    });

    // Currently we deploy tools as self-contained .NET Core binaries for macOS only!
    const toolsOrchestrator = pack({
        id: `${packageNamePrefix}.Tools.Orchestrator.osx-x64`,
        deployment: Tools.Orchestrator.withQualifier({
            configuration: qualifier.configuration,
            targetFramework: "netcoreapp3.0",
            targetRuntime: "osx-x64"
        }).deployment
    });

    @@public
    export const deployment : Deployment.Definition = {
        contents: [
            ...addIfLazy(canBuildAllPackagesOnThisHost, () => [
                net472,
                ...addIf(!BuildXLSdk.Flags.genVSSolution,
                    winX64
                ),
                cacheTools,
                cacheLibraries,
                cacheInterfaces,
                cacheHashing,
            ]),
            sdks,
            ...addIf(!BuildXLSdk.Flags.genVSSolution, osxX64, toolsOrchestrator),
            toolsSandBoxExec,
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: packageTargetFolder,
    });

    export function pack(args: {id: string, deployment: Deployment.Definition, copyContentFiles?: boolean, dependencies?: (Nuget.Dependency | Managed.ManagedNugetPackage)[]}) : File {
        const dependencies : Nuget.Dependency[] = (args.dependencies || [])
            .map(dep => {
                if (isManagedPackage(dep)) {
                    return {id: dep.name, version: dep.version};
                } else {
                    return dep;
                }
            });

        return Nuget.pack({
            metadata:  {
                id: args.id,
                version: Branding.Nuget.packageVersion,
                authors: Branding.Nuget.packageAuthors,
                owners: Branding.Nuget.packageOwners,
                copyright: Branding.Nuget.pacakgeCopyright,
                tags: `${Branding.company} ${Branding.shortProductName} MSBuild Build`,
                description: `${Branding.shortProductName} is a build engine that comes with a new build automation language. ${Branding.shortProductName} performs fast parallel incremental builds enabled by fine-grained dataflow dependency information. All build artifacts are cached locally, and eventually shared between different machines. The engine can run on a single machine, and it will perform distributed builds on many machines in a lab or in the cloud.`,
                dependencies: dependencies,
                contentFiles: args.copyContentFiles
                    ? [{
                        include: "**",
                        copyToOutput: true,
                        buildAction: "None",
                      }]
                    : undefined,
            },
            deployment: args.deployment,
            noPackageAnalysis: true,
            noDefaultExcludes: true,
        }).nuPkg;
    }

    export function isManagedPackage(item: Nuget.Dependency | Managed.ManagedNugetPackage) : item is Managed.ManagedNugetPackage {
        return item["compile"] !== undefined || item["runtime"] !== undefined || item["runtimeContent"] !== undefined || item["analyzers"] !== undefined;
    }
}
