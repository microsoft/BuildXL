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
    
    const net472PackageQualifer = {
        targetFramework: "net472",
        targetRuntime: "win-x64"
    };
    
    const net6PackageQualifer = {
        targetFramework: "net6.0",
        targetRuntime: "win-x64"
    };

    const netstandard20PackageQualifer = {
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
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).dll,

            // BuildXL.Utilities.Branding
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).Branding.dll,

            // BuildXL.Collections
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Collections.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Collections.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).Collections.dll,

            // BuildXL.Interop
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Interop.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Interop.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).Interop.dll,

            // BuildXL.KeyValueStore
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).KeyValueStore.dll,

            // BuildXL.Native
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).Native.dll,

            // BuildXL.Configuration
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).Configuration.dll,

            // BuildXL.SBOMUtilities
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, 
                importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).SBOMUtilities.dll,
                importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).SBOMUtilities.dll,
                importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).SBOMUtilities.dll
            ),

            // BuildXL.Instrumentation.Common
            importFrom("BuildXL.Utilities.Instrumentation").Common.withQualifier(net472PackageQualifer).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.withQualifier(net6PackageQualifer).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Common.withQualifier(netstandard20PackageQualifer).dll,

            // BuildXL.Instrumentation.Tracing
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net472PackageQualifer).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net6PackageQualifer).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(netstandard20PackageQualifer).dll,

            // BuildXL.Utilities.Authentication
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, 
                importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Authentication.dll
            ),
        ],
        dependencies: [
            // The package gen does not automatically handle locally build dependencies since we don't know in which package they go yet
            // Therefore for now we manually declare these.
            buildXLCacheHashingIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions,
        additionalContent: [
            {
                subfolder: r`runtimes/win-x64/native/`,
                contents: [
                    ...importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifer).Native.nativeWin,
                ],
            },
            {
                subfolder: r`runtimes/osx-x64/native/`,
                contents: [
                    ...importFrom("BuildXL.Utilities").withQualifier(osxPackageQualifier).Native.nativeMac,
                ],
            },
            {
                subfolder: r`runtimes/linux-x64/native/`,
                contents: [
                    ...importFrom("BuildXL.Utilities").withQualifier(linuxPackageQualifier).Native.nativeLinux,
                ],
            },
        ]
    });

    const pips = packAssemblies({
        id: buildXLPipsIdentity.id,
        assemblies: [
            // BuildXL.Utilities
            importFrom("BuildXL.Pips").withQualifier(net472PackageQualifer).dll,
            importFrom("BuildXL.Pips").withQualifier(net6PackageQualifer).dll,

            // BuildXL.Ipc
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Ipc.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Ipc.dll,

            // BuildXL.Storage
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Storage.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Storage.dll,
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
            importFrom("BuildXL.Engine").withQualifier(net472PackageQualifer).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net6PackageQualifer).Processes.dll,
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
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472PackageQualifer).InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472PackageQualifer).Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472PackageQualifer).BasicFilesystem.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472PackageQualifer).BuildCacheAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472PackageQualifer).MemoizationStoreAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472PackageQualifer).VerticalAggregator.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net472PackageQualifer).ImplementationSupport.dll,
            importFrom("BuildXL.Utilities").withQualifier(net472PackageQualifer).Storage.dll,
            
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifer).InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifer).Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifer).BasicFilesystem.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifer).BuildCacheAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifer).MemoizationStoreAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifer).VerticalAggregator.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net6PackageQualifer).ImplementationSupport.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifer).Storage.dll,
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

            importFrom("Microsoft.Azure.EventHubs").withQualifier(net472PackageQualifer).pkg,
            importFrom("Microsoft.Azure.Amqp").withQualifier(net472PackageQualifer).pkg,
            importFrom("System.Threading.Tasks.Dataflow").withQualifier(net472PackageQualifer).pkg,
            ...BuildXLSdk.withQualifier(net472PackageQualifer).bclAsyncPackages,
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472PackageQualifer).getGrpcPackagesWithoutNetStandard(),
            // Including the following reference is the most correct thing to do, but it causes a conflict in NuGet 
            // because we reference things inconsistently. If someone depends on the ProtoBuf.Net functionality, they 
            // must themselves refer to the required packages.
            // ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472PackageQualifer).getProtobufNetPackages(false),
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472PackageQualifer).getSerializationPackagesWithoutNetStandard(),
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472PackageQualifer).getSystemTextJsonWithoutNetStandard(),
            importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").withQualifier(net472PackageQualifer).pkg,
            importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").withQualifier(net6PackageQualifer).pkg,
            importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").withQualifier(net472PackageQualifer).pkg,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").withQualifier(net472PackageQualifer).getRocksDbPackagesWithoutNetStandard(),
            importFrom("NLog").withQualifier(net472PackageQualifer).pkg,
            importFrom("Polly").withQualifier(net472PackageQualifer).pkg,
            importFrom("Polly.Contrib.WaitAndRetry").withQualifier(net472PackageQualifer).pkg,
        ]
    });

    const cacheInterfaces = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: buildXLCacheInterfacesIdentity.id,
        deployment: Cache.NugetPackages.interfaces,
        dependencies: [
            buildXLCacheHashingIdentity,
            buildXLUtilitiesIdentity,

            importFrom("System.Threading.Tasks.Dataflow").withQualifier(net472PackageQualifer).pkg,
            ...BuildXLSdk.withQualifier(net472PackageQualifer).bclAsyncPackages,
            importFrom("WindowsAzure.Storage").withQualifier(net472PackageQualifer).pkg,
        ]
    });

    const cacheHashing = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: buildXLCacheHashingIdentity.id,
        deployment: Cache.NugetPackages.hashing,
        dependencies: [
            ...BuildXLSdk.withQualifier(net472PackageQualifer).bclAsyncPackages,
            importFrom("System.Threading.Tasks.Dataflow").withQualifier(net472PackageQualifer).pkg,
            importFrom("RuntimeContracts").withQualifier(net472PackageQualifer).pkg,
            importFrom("System.Memory").withQualifier(net472PackageQualifer).pkg,
            importFrom("System.Threading.Tasks.Extensions").withQualifier(net472PackageQualifer).pkg,
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
                engineCache
            ]),
            sdks,
            ...addIf(!BuildXLSdk.Flags.genVSSolution, osxX64, linuxX64, toolsAdoBuildRunner),
            toolsSandBoxExec
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
