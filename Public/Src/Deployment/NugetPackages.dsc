// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Branding from "BuildXL.Branding";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release" };

    const net472PackageQualifer = {
        targetFramework: "net472",
        targetRuntime: "win-x64"
    };

    const canBuildAllPackagesOnThisHost = Context.getCurrentHost().os === "win";

    const packageNamePrefix = BuildXLSdk.Flags.isMicrosoftInternal
        ? "BuildXL"
        : "Microsoft.BuildXL";

    const packageTargetFolder = BuildXLSdk.Flags.isMicrosoftInternal
        ? r`${qualifier.configuration}/pkgs`
        : r`${qualifier.configuration}/public/pkgs`;

    const reducedDeploymentOptions: Managed.Deployment.FlattenOptions = {
        skipPdb: false,
        skipXml: true,
    };

    const winX64 = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.win-x64`,
        deployment: BuildXL.withQualifier({
            targetFramework: "netcoreapp3.1",
            targetRuntime: "win-x64"
        }).deployment,
        deploymentOptions: reducedDeploymentOptions
    });

    const osxX64 = pack({
        id: `${packageNamePrefix}.osx-x64`,
        deployment: BuildXL.withQualifier({
            targetFramework: "netcoreapp3.1",
            targetRuntime: "osx-x64"
        }).deployment,
        deploymentOptions: reducedDeploymentOptions
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

            importFrom("Microsoft.Tpl.Dataflow").withQualifier(net472PackageQualifer).pkg,
            importFrom("System.Interactive.Async").withQualifier(net472PackageQualifer).pkg,
            importFrom("Grpc.Core").withQualifier(net472PackageQualifer).pkg,
            importFrom("Google.Protobuf").withQualifier(net472PackageQualifer).pkg,
            importFrom("StackExchange.Redis.StrongName").withQualifier(net472PackageQualifer).pkg,
            importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").withQualifier(net472PackageQualifer).pkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").withQualifier(net472PackageQualifer).pkg,
        ]
    });

    const cacheInterfaces = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.Cache.Interfaces`,
        deployment: Cache.NugetPackages.interfaces,
        dependencies: [
            { id: `${packageNamePrefix}.Cache.Hashing`, version: Branding.Nuget.packageVersion},

            importFrom("Microsoft.Tpl.Dataflow").withQualifier(net472PackageQualifer).pkg,
            importFrom("System.Interactive.Async").withQualifier(net472PackageQualifer).pkg,
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
            targetFramework: "netcoreapp3.1",
            targetRuntime: "osx-x64"
        }).deployment
    });

    // Currently we deploy tools as self-contained .NET Core binaries for macOS only!
    const toolsOrchestrator = pack({
        id: `${packageNamePrefix}.Tools.Orchestrator.osx-x64`,
        deployment: Tools.Orchestrator.withQualifier({
            targetFramework: "netcoreapp3.1",
            targetRuntime: "osx-x64"
        }).deployment
    });

    const xldbnetcorequalifier : BuildXLSdk.DefaultQualifierWithNet472 = {
        targetFramework: "netcoreapp3.1",
        configuration: qualifier.configuration,
        targetRuntime: "win-x64"
    };

    const xldbnet472qualifier : BuildXLSdk.DefaultQualifierWithNet472 = {
        targetFramework: "net472",
        configuration: qualifier.configuration,
        targetRuntime: "win-x64"
    };

    const xldblibrary = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.Xldb`,
        deployment: {
            contents: [
                Nuget.createAssemblyLayout(importFrom("BuildXL.Tools").Xldb.withQualifier(xldbnetcorequalifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Tools").Xldb.Proto.withQualifier(xldbnetcorequalifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(xldbnetcorequalifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").KeyValueStore.withQualifier(xldbnetcorequalifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").Collections.withQualifier(xldbnetcorequalifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").Native.withQualifier(xldbnetcorequalifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").Storage.withQualifier(xldbnetcorequalifier).dll),

                Nuget.createAssemblyLayout(importFrom("BuildXL.Tools").Xldb.withQualifier(xldbnet472qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Tools").Xldb.Proto.withQualifier(xldbnet472qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").withQualifier(xldbnet472qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").KeyValueStore.withQualifier(xldbnet472qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").Collections.withQualifier(xldbnet472qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").Native.withQualifier(xldbnet472qualifier).dll),
                Nuget.createAssemblyLayout(importFrom("BuildXL.Utilities").Storage.withQualifier(xldbnet472qualifier).dll),

                {
                    subfolder: r`content`,
                    contents: [
                        importFrom("BuildXL.Sandbox.Windows").Deployment.withQualifier({ targetRuntime: "win-x64" }).natives,
                    ]
                },
            ]
        },
        dependencies: [
            importFrom("RocksDbSharpSigned").withQualifier(net472PackageQualifer).pkg,
            importFrom("RocksDbNative").withQualifier(net472PackageQualifer).pkg,
            importFrom("Google.Protobuf").withQualifier(net472PackageQualifer).pkg,
            importFrom("RuntimeContracts").withQualifier(net472PackageQualifer).pkg,
        ],
    });

    const deployment : Deployment.Definition = {
        contents: [
            ...addIfLazy(canBuildAllPackagesOnThisHost, () => [
                ...addIf(!BuildXLSdk.Flags.genVSSolution,
                    winX64
                ),
                cacheTools,
                cacheLibraries,
                cacheInterfaces,
                cacheHashing,
                xldblibrary,
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

    export function pack(args: {
        id: string,
        deployment: Deployment.Definition,
        deploymentOptions?: Managed.Deployment.FlattenOptions,
        copyContentFiles?: boolean,
        dependencies?: (Nuget.Dependency | Managed.ManagedNugetPackage)[]
    }) : File {

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
            deploymentOptions: args.deploymentOptions,
            noPackageAnalysis: true,
            noDefaultExcludes: true,
        }).nuPkg;
    }

    export function isManagedPackage(item: Nuget.Dependency | Managed.ManagedNugetPackage) : item is Managed.ManagedNugetPackage {
        return item["compile"] !== undefined || item["runtime"] !== undefined || item["runtimeContent"] !== undefined || item["analyzers"] !== undefined;
    }
}
