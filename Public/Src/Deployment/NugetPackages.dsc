// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Branding from "BuildXL.Branding";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";
import * as Nuget from "Sdk.Managed.Tools.NuGet";

namespace NugetPackages {
    export declare const qualifier : { configuration: "debug" | "release" };
    const defaultTargetFramework = Managed.TargetFrameworks.DefaultTargetFramework;
    
    const net472packageQualifier = {
        targetFramework: "net472",
        targetRuntime: "win-x64"
    };
    
    const net6PackageQualifier = {
        targetFramework: "net6.0",
        targetRuntime: "win-x64"
    };

    const net7PackageQualifier = {
        targetFramework: "net7.0",
        targetRuntime: "win-x64"
    };

    const netstandard20PackageQualifier = {
        targetFramework: "netstandard2.0",
        targetRuntime: "win-x64"
    };

    const osxPackageQualifier = { targetFramework: "netstandard2.0", targetRuntime: "osx-x64" };
    const linuxPackageQualifier = { targetFramework: "netstandard2.0", targetRuntime: "linux-x64" };

    const canBuildAllPackagesOnThisHost = Context.getCurrentHost().os === "win";

    const packageNamePrefix = BuildXLSdk.Flags.isMicrosoftInternal
        ? "BuildXL"
        : "Microsoft.BuildXL";

    const buildXLUtilitiesIdentity = { id: `${packageNamePrefix}.Utilities`, version: Branding.Nuget.packageVersion};
    const buildXLPipsIdentity = { id: `${packageNamePrefix}.Pips`, version: Branding.Nuget.packageVersion};
    const buildXLCacheHashingIdentity = { id: `${packageNamePrefix}.Cache.Hashing`, version: Branding.Nuget.packageVersion};
    const buildXLCacheInterfacesIdentity = { id: `${packageNamePrefix}.Cache.Interfaces`, version: Branding.Nuget.packageVersion};
    const buildXLCacheLibrariesIdentity = { id: `${packageNamePrefix}.Cache.Libraries`, version: Branding.Nuget.packageVersion};
    const buildXLCacheServiceIdentity = { id: `${packageNamePrefix}.Cache.Service`, version: Branding.Nuget.packageVersion};

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
            targetFramework: defaultTargetFramework,
            targetRuntime: "win-x64"
        }).deployment,
        deploymentOptions: reducedDeploymentOptions,
        // The following PDBs are quite big and copied many times. Remove them from the nuget
        // package to save some space.
        filterFiles: [a`DetoursServices.pdb`, a`BuildXLAria.pdb`, a`BuildXLNatives.pdb`]
    });

    const osxX64 = pack({
        id: `${packageNamePrefix}.osx-x64`,
        deployment: BuildXL.withQualifier({
            targetFramework: defaultTargetFramework,
            targetRuntime: "osx-x64"
        }).deployment,
        deploymentOptions: reducedDeploymentOptions
    });

    const linuxX64 = pack({
        id: `${packageNamePrefix}.linux-x64`,
        deployment: BuildXL.withQualifier({
            targetFramework: defaultTargetFramework,
            targetRuntime: "linux-x64"
        }).deployment,
        deploymentOptions: reducedDeploymentOptions
    });

    const sdks = pack({
        id: `${packageNamePrefix}.Sdks`,
        deployment: Sdks.deployment,
    });

    const utilities = packAssemblies({
        id: buildXLUtilitiesIdentity.id,
        assemblies: [
            // BuildXL.Utilities
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).dll,

            // BuildXL.Utilities.Branding
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Branding.dll,

            // BuildXL.Collections
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Collections.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Collections.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Collections.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Collections.dll,

            // BuildXL.Interop
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Interop.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Interop.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Interop.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Interop.dll,

            // BuildXL.KeyValueStore
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).KeyValueStore.dll,

            // BuildXL.Native
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Native.dll,

            // BuildXL.Configuration
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Configuration.dll,

            // BuildXL.SBOMUtilities
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, 
                importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).SBOMUtilities.dll,
                importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).SBOMUtilities.dll
            ),

            // BuildXL.Instrumentation.Common
            importFrom("BuildXL.Utilities.Instrumentation").Common.withQualifier(net472packageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.withQualifier(net6PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.withQualifier(net7PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.withQualifier(netstandard20PackageQualifier).dll,

            // BuildXL.Instrumentation.Tracing
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net472packageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net6PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net7PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(netstandard20PackageQualifier).dll,

            // BuildXL.Utilities.Authentication
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, 
                importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Authentication.dll
            ),
        ],
        dependencies: [
            // The package gen does not automatically handle locally build dependencies since we don't know in which package they go yet
            // Therefore for now we manually declare these.
            buildXLCacheHashingIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions,
        additionalContent: [
            ...addIfLazy(Context.getCurrentHost().os === "win", () => [{
                subfolder: r`runtimes/win-x64/native/`,
                contents: [
                    ...importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Native.nativeWin,
                ],
            }]),
            ...addIfLazy(Context.getCurrentHost().os === "macOS", () => [{
                subfolder: r`runtimes/osx-x64/native/`,
                contents: [
                    ...importFrom("BuildXL.Utilities").withQualifier(osxPackageQualifier).Native.nativeMac,
                ],
            }]),
            ...addIfLazy(Context.getCurrentHost().os === "unix", () => [{
                subfolder: r`runtimes/linux-x64/native/`,
                contents: [
                    ...importFrom("BuildXL.Utilities").withQualifier(linuxPackageQualifier).Native.nativeLinux,
                ],
            }]),
        ]
    });

    const pips = packAssemblies({
        id: buildXLPipsIdentity.id,
        assemblies: [
            // BuildXL.Utilities
            importFrom("BuildXL.Pips").withQualifier(net472packageQualifier).dll,
            importFrom("BuildXL.Pips").withQualifier(net6PackageQualifier).dll,
            importFrom("BuildXL.Pips").withQualifier(net7PackageQualifier).dll,

            // BuildXL.Ipc
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Ipc.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Ipc.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Ipc.dll,

            // BuildXL.Storage
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Storage.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Storage.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Storage.dll,
        ],
        dependencies: [
            // The package gen does not automatically handle locally build dependencies since we don't know in which package they go yet
            // Therefore for now we manually declare these.
            buildXLUtilitiesIdentity,
            buildXLCacheHashingIdentity,
            buildXLCacheInterfacesIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions,
    });

    const processes = packAssemblies({
        id: `${packageNamePrefix}.Processes`,
        assemblies: [
            // BuildXL.Processes
            importFrom("BuildXL.Engine").withQualifier(net472packageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net6PackageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net7PackageQualifier).Processes.dll,
        ],
        dependencies: [
            // The package gen does not automatically handle locally build dependencies since we don't know in which package they go yet
            // Therefore for now we manually declare these.
            buildXLUtilitiesIdentity,
            buildXLPipsIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions,
    });

    const engineCache = packAssemblies({
        id: `${packageNamePrefix}.Engine.Cache`,
        assemblies: [
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472packageQualifier).InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472packageQualifier).Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472packageQualifier).BasicFilesystem.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472packageQualifier).BuildCacheAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472packageQualifier).MemoizationStoreAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472packageQualifier).VerticalAggregator.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472packageQualifier).ImplementationSupport.dll,
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Storage.dll,
            
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifier).InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifier).Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifier).BasicFilesystem.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifier).BuildCacheAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifier).MemoizationStoreAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifier).VerticalAggregator.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifier).ImplementationSupport.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Storage.dll,

            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net7PackageQualifier).InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net7PackageQualifier).Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net7PackageQualifier).BasicFilesystem.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net7PackageQualifier).BuildCacheAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net7PackageQualifier).MemoizationStoreAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net7PackageQualifier).VerticalAggregator.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net7PackageQualifier).ImplementationSupport.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Storage.dll,
        ],
        dependencies: [
            // The package gen does not automatically handle locally build dependencies since we don't know in which package they go yet
            // Therefore for now we manually declare these.
            buildXLUtilitiesIdentity,
            buildXLCacheHashingIdentity,
            buildXLCacheInterfacesIdentity,
            buildXLCacheLibrariesIdentity,
        ]
    });

    const cacheServiceDeployment : Deployment.Definition = {
        contents: [
            {
                subfolder: r`tools`,
                // CacheService is a .NET CORE-only application.
                contents: [
                    {
                        subfolder: r`net6.0`,
                        contents: [importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: defaultTargetFramework, targetRuntime: "win-x64" }).LauncherServer.exe]
                    },
                    {
                        subfolder: r`net7.0`,
                        contents: [importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net7.0", targetRuntime: "win-x64" }).LauncherServer.exe]
                    }
                ]
            }
        ],
    };

    const cacheService = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.CacheService.win-x64`,
        deployment: cacheServiceDeployment,
        deploymentOptions: reducedDeploymentOptions
    });

    const cacheTools = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.Cache.Tools`,
        deployment: Cache.NugetPackages.tools,
    });

    const cacheLibraries = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: buildXLCacheLibrariesIdentity.id,
        deployment: Cache.NugetPackages.libraries,
        dependencies: [
            buildXLCacheInterfacesIdentity,
            buildXLUtilitiesIdentity,

            importFrom("Microsoft.Azure.EventHubs").withQualifier(net472packageQualifier).pkg,
            importFrom("Microsoft.Azure.Amqp").withQualifier(net472packageQualifier).pkg,
            importFrom("System.Threading.Tasks.Dataflow").withQualifier(net472packageQualifier).pkg,
            ...BuildXLSdk.withQualifier(net472packageQualifier).bclAsyncPackages,
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472packageQualifier).getGrpcPackagesWithoutNetStandard(),
            // Including the following reference is the most correct thing to do, but it causes a conflict in NuGet 
            // because we reference things inconsistently. If someone depends on the ProtoBuf.Net functionality, they 
            // must themselves refer to the required packages.
            // ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472packageQualifier).getProtobufNetPackages(false),
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472packageQualifier).getSerializationPackagesWithoutNetStandard(),
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472packageQualifier).getSystemTextJsonWithoutNetStandard(),
            importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").withQualifier(net472packageQualifier).pkg,
            importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").withQualifier(net6PackageQualifier).pkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").withQualifier(net472packageQualifier).pkg,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").withQualifier(net472packageQualifier).getRocksDbPackagesWithoutNetStandard(),
            importFrom("NLog").withQualifier(net472packageQualifier).pkg,
            importFrom("Polly").withQualifier(net472packageQualifier).pkg,
            importFrom("Polly.Contrib.WaitAndRetry").withQualifier(net472packageQualifier).pkg,
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472packageQualifier).getAzureBlobStorageSdkPackagesWithoutNetStandard(),
        ]
    });

    const cacheInterfaces = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: buildXLCacheInterfacesIdentity.id,
        deployment: Cache.NugetPackages.interfaces,
        dependencies: [
            buildXLCacheHashingIdentity,
            buildXLUtilitiesIdentity,

            importFrom("System.Threading.Tasks.Dataflow").withQualifier(net472packageQualifier).pkg,
            ...BuildXLSdk.withQualifier(net472packageQualifier).bclAsyncPackages,
            importFrom("WindowsAzure.Storage").withQualifier(net472packageQualifier).pkg,
        ]
    });

    const cacheHashing = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: buildXLCacheHashingIdentity.id,
        deployment: Cache.NugetPackages.hashing,
        dependencies: [
            ...BuildXLSdk.withQualifier(net472packageQualifier).bclAsyncPackages,
            importFrom("System.Threading.Tasks.Dataflow").withQualifier(net472packageQualifier).pkg,
            importFrom("RuntimeContracts").withQualifier(net472packageQualifier).pkg,
            importFrom("System.Memory").withQualifier(net472packageQualifier).pkg,
            importFrom("System.Threading.Tasks.Extensions").withQualifier(net472packageQualifier).pkg,
        ]
    });

    // Currently we deploy tools as self-contained .NET Core binaries for macOS only!
    const toolsSandBoxExec = pack({
        id: `${packageNamePrefix}.Tools.SandboxExec.osx-x64`,
        deployment: Tools.SandboxExec.withQualifier({
            targetFramework: defaultTargetFramework,
            targetRuntime: "osx-x64"
        }).deployment
    });

    // Currently we deploy tools as self-contained .NET Core binaries for macOS only!
    const toolsAdoBuildRunner = pack({
        id: `${packageNamePrefix}.Tools.AdoBuildRunner.osx-x64`,
        deployment: Tools.AdoBuildRunner.withQualifier({
            targetFramework: defaultTargetFramework,
            targetRuntime: "osx-x64"
        }).deployment
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
                cacheService,
                cacheHashing,
                utilities,
                pips,
                processes,
                engineCache,
                sdks,
                // macOS specific packages
                toolsSandBoxExec,
                osxX64,
                toolsAdoBuildRunner,
            ]),
            ...addIfLazy(!BuildXLSdk.Flags.genVSSolution && Context.getCurrentHost().os === "unix", () => [
                linuxX64
            ]),
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: packageTargetFolder,
    });

    /**
     * Helper to pack assemblies. 
     * We'll keep it in the BuildXL side for now. 
     * If useful we should move it to the Nuget sdk.
     */
    function packAssemblies(args: {
        id: string,
        assemblies: Managed.Assembly[],
        dependencies?: Nuget.Dependency[],
        deploymentOptions?: Managed.Deployment.FlattenOptions,
        additionalContent?: Deployment.DeployableItem[],
    }) : File
    {
        let dependencies : Nuget.Dependency[] = args
            .assemblies
            .filter(asm => asm !== undefined)
            .mapMany(asm => asm
            .references
                .filter(ref => Managed.isManagedPackage(ref))
                .map(ref => <Managed.ManagedNugetPackage>ref)
                .map(ref => { return {id: ref.name, version: ref.version, targetFramework: asm.targetFramework}; })
                .concat( (args.dependencies || []).map(dep => { return {id: dep.id, version: dep.version, targetFramework: asm.targetFramework }; }) )
            );
        
        // If we ever add support for Mac pacakges here, we will have a problem because nuget does not
        // support our scenario as of Jan 2020.
        //  * We can't use contentFiles/any/{tfm} pattern because it doesn't support {rid}
        //  * We can't place stuff in runtimes/{rid}/lib/{tfm}/xxxx nor in runtimes/{rid}/native/xxxx beause:
        //        a) packages.config projects don't support the runtimes folder
        //        b) nuget does not copy files on build. So F5 and unittests are broken. One has to hit 'publish'
        //        c) nuget does not copy subfolders under those
        // So the only solution is to include a custom targets file, which is hard to write because now that
        // targets file is responsible for doing the {rid} graph resolution between win10-x64, win10, win-x64 etc.
        // Therefore we will stick to only supporting windows platform and using contentFiles pattern
        let contentFiles : Deployment.DeployableItem[] = args
            .assemblies
            .filter(asm => asm !== undefined && asm.runtimeContent !== undefined)
            .map(asm => <Deployment.NestedDefinition>{
                // Note since all windows tfms have the same content, we are manually
                // if we ever create differences between tmfs, we will have to change the second 
                // any to ${asm.targetFramework}
                subfolder: r`contentFiles/any/any`,
                contents: [
                    asm.runtimeContent
                ]
            });

        return Nuget.pack({
            metadata:  createMetaData({
                id: args.id, 
                dependencies: dependencies, 
                copyContentFiles: contentFiles.length > 0,
            }),
            deployment: {
                contents: [
                    ...args.assemblies.map(asm => Nuget.createAssemblyLayout(asm)),
                    ...contentFiles,
                    ...args.additionalContent || [],
                ]
            },
            deploymentOptions: args.deploymentOptions,
            noPackageAnalysis: true,
            noDefaultExcludes: true,
        }).nuPkg;
    }

    export function pack(args: {
        id: string,
        deployment: Deployment.Definition,
        deploymentOptions?: Managed.Deployment.FlattenOptions,
        copyContentFiles?: boolean,
        dependencies?: (Nuget.Dependency | Managed.ManagedNugetPackage)[],
        filterFiles?: PathAtom[]
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
            metadata:  createMetaData({id: args.id, dependencies: dependencies,copyContentFiles: args.copyContentFiles}),
            deployment: args.deployment,
            deploymentOptions: args.deploymentOptions,
            noPackageAnalysis: true,
            noDefaultExcludes: true,
            filterFiles: args.filterFiles,
        }).nuPkg;
    }

    export function createMetaData(args: {
        id: string,
        dependencies: Nuget.Dependency[],
        copyContentFiles?: boolean,
    }) : Nuget.PackageMetadata
    {
        return {
            id: args.id,
            version: Branding.Nuget.packageVersion,
            authors: Branding.Nuget.packageAuthors,
            owners: Branding.Nuget.packageOwners,
            copyright: Branding.Nuget.pacakgeCopyright,
            tags: `${Branding.company} ${Branding.shortProductName} MSBuild Build`,
            description: `${Branding.shortProductName} is a build engine that comes with a new build automation language. ${Branding.shortProductName} performs fast parallel incremental builds enabled by fine-grained dataflow dependency information. All build artifacts are cached locally, and eventually shared between different machines. The engine can run on a single machine, and it will perform distributed builds on many machines in a lab or in the cloud.`,
            dependencies: args.dependencies,
            contentFiles: args.copyContentFiles
                ? [{
                    include: "**",
                    copyToOutput: true,
                    buildAction: "None",
                    }]
                : undefined,
        };
    }

    export function isManagedPackage(item: Nuget.Dependency | Managed.ManagedNugetPackage) : item is Managed.ManagedNugetPackage {
        return item["compile"] !== undefined || item["runtime"] !== undefined || item["runtimeContent"] !== undefined || item["analyzers"] !== undefined;
    }
}
