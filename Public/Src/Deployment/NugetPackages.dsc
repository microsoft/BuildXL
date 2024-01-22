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
    
    /**
     * Readme before adding a new package here.
     * 
     * 1. Whenever possible, ensure that a Nuget package only contains a single assembly.
     * 2. Create a package identifier with a name that matches the name of the assembly being packaged.
     * 3. Create a Nuget.PackageSpecification for the new package.
     * 4. Add the PackageSpecification into the `packageSpecifications` array.
     * 5. Call `Nuget.packAssemblies` with inferInternalDependencies set to true to enable package dependency verification.
     */

    // Windows Qualifiers
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

    // macOS Qualifiers
    const osxPackageQualifier = { targetFramework: "netstandard2.0", targetRuntime: "osx-x64" };

    // Linux Qualifiers
    const netStandardLinuxPackageQualifier = {
        targetFramework: "netstandard2.0",
        targetRuntime: "linux-x64"
    };

    const net6LinuxPackageQualifier = {
        targetFramework: "net6.0",
        targetRuntime: "linux-x64"
    };
    
    const net7LinuxPackageQualifier = {
        targetFramework: "net7.0",
        targetRuntime: "linux-x64"
    };

    const canBuildAllPackagesOnThisHost = Context.getCurrentHost().os === "win";

    const packageNamePrefix = BuildXLSdk.Flags.isMicrosoftInternal
        ? "BuildXL"
        : "Microsoft.BuildXL";

    const buildXLAriaCommonIdentity = { id: `${packageNamePrefix}.AriaCommon`, version: Branding.Nuget.packageVersion };
    const buildXLUtilitiesIdentity = { id: `${packageNamePrefix}.Utilities`, version: Branding.Nuget.packageVersion };
    const buildXLUtilitiesCoreIdentity = { id: `${packageNamePrefix}.Utilities.Core`, version: Branding.Nuget.packageVersion };
    const buildXLNativeIdentity = { id: `${packageNamePrefix}.Native`, version: Branding.Nuget.packageVersion };
    const buildXLPipsIdentity = { id: `${packageNamePrefix}.Pips`, version: Branding.Nuget.packageVersion };

    // Old cache packages to be phased out
    const buildXLCacheHashingIdentity = { id: `${packageNamePrefix}.Cache.Hashing`, version: Branding.Nuget.packageVersion };
    const buildXLCacheInterfacesIdentity = { id: `${packageNamePrefix}.Cache.Interfaces`, version: Branding.Nuget.packageVersion };
    const buildXLCacheLibrariesIdentity = { id: `${packageNamePrefix}.Cache.Libraries`, version: Branding.Nuget.packageVersion };
    const buildXLCacheServiceIdentity = { id: `${packageNamePrefix}.Cache.Service`, version: Branding.Nuget.packageVersion };

    // Cache Packages
    const buildXLContentStoreDistributedIdentity = { id: `${packageNamePrefix}.Cache.ContentStore.Distributed`, version: Branding.Nuget.packageVersion };
    const buildXLContentStoreLibraryIdentity = { id: `${packageNamePrefix}.Cache.ContentStore.Library`, version: Branding.Nuget.packageVersion };
    const buildXLContentStoreGrpcIdentity = { id: `${packageNamePrefix}.Cache.ContentStore.Grpc`, version: Branding.Nuget.packageVersion };
    const buildXLContentStoreVstsIdentity = { id: `${packageNamePrefix}.Cache.ContentStore.Vsts`, version: Branding.Nuget.packageVersion };
    const buildXLContentStoreVstsInterfacesIdentity = { id: `${packageNamePrefix}.Cache.ContentStore.VstsInterfaces`, version: Branding.Nuget.packageVersion };
    const buildXLMemoizationStoreDistributedIdentity = { id: `${packageNamePrefix}.Cache.MemoizationStore.Distributed`, version: Branding.Nuget.packageVersion };
    const buildXLMemoizationStoreLibraryIdentity = { id: `${packageNamePrefix}.Cache.MemoizationStore.Library`, version: Branding.Nuget.packageVersion };
    const buildXLMemoizationStoreVstsIdentity = { id: `${packageNamePrefix}.Cache.MemoizationStore.Vsts`, version: Branding.Nuget.packageVersion };
    const buildXLMemoizationStoreVstsInterfacesIdentity = { id: `${packageNamePrefix}.Cache.MemoizationStore.VstsInterfaces`, version: Branding.Nuget.packageVersion };
    const buildXLCacheHostServicesIdentity = { id: `${packageNamePrefix}.Cache.DistributedCacheHost.Service`, version: Branding.Nuget.packageVersion };
    const buildXLCacheHostConfigurationIdentity = { id: `${packageNamePrefix}.Cache.DistributedCacheHost.Configuration`, version: Branding.Nuget.packageVersion };
    const buildXLCacheLoggingIdentity = { id: `${packageNamePrefix}.Cache.Logging`, version: Branding.Nuget.packageVersion };
    const buildXLContentStoreInterfacesIdentity = { id: `${packageNamePrefix}.Cache.ContentStore.Interfaces`, version: Branding.Nuget.packageVersion };
    const buildXLMemoizationStoreInterfacesIdentity = { id: `${packageNamePrefix}.Cache.MemoizationStore.Interfaces`, version: Branding.Nuget.packageVersion };
    const buildXLContentStoreHashingIdentity = { id: `${packageNamePrefix}.Cache.ContentStore.Hashing`, version: Branding.Nuget.packageVersion };
    const buildXLContentStoreUtilitiesCoreIdentity = { id: `${packageNamePrefix}.Cache.ContentStore.UtilitiesCore`, version: Branding.Nuget.packageVersion };
    const buildXLBlobLifetimeManagerIdentity = { id: `${packageNamePrefix}.Cache.BlobLifetimeManager.Library`, version: Branding.Nuget.packageVersion };

    // External packages
    // The macOS runtime package is only produced publicly, so 'Microsoft.BuildXL' will always be its prefix (ie: instead of using `packageNamePrefix`)
    // To produce a new version of this package please refer to our internal BuildXL OneNote in the macOS section.
    const buildXLMacOSRuntimeIdentity = { id: `Microsoft.BuildXL.Interop.Runtime.osx-x64`, version: Branding.Nuget.packageVersion };

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
    
    const winX64Net7 = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: `${packageNamePrefix}.win-x64-net7`,
        deployment: BuildXL.withQualifier(net7PackageQualifier).deployment,
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
    
    const linuxX64Net7 = pack({
        id: `${packageNamePrefix}.linux-x64-net7`,
        deployment: BuildXL.withQualifier(net7LinuxPackageQualifier).deployment,
        deploymentOptions: reducedDeploymentOptions
    });

    const sdks = pack({
        id: `${packageNamePrefix}.Sdks`,
        deployment: Sdks.deployment,
    });

    // BuildXL.AriaCommon
    const ariaCommonSpecification : Nuget.PackageSpecification = {
        id: buildXLAriaCommonIdentity,
        assemblies: [
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(net472packageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(net6PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(net7PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(netstandard20PackageQualifier).dll,
        ]
    };

    const utilitiesSpecification : Nuget.PackageSpecification = {
        id: buildXLUtilitiesIdentity,
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

            // BuildXL.KeyValueStore
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).KeyValueStore.dll,

            // BuildXL.Native.Extensions
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Native.Extensions.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Native.Extensions.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Native.Extensions.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Native.Extensions.dll,

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
            // This package has contains multiple assemblies so for now we will manually declare its dependencies
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,
            // When making a change to the macOS interop dylib, ensure that a new version of this package is built
            // and the version number for this identity is updated.
            // For more details check the macOS section on the BuildXL onenote.
            buildXLMacOSRuntimeIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions,
        additionalContent: [
            ...addIfLazy(Context.getCurrentHost().os === "win", () => [{
                subfolder: r`runtimes/win-x64/native/`,
                contents: [
                    ...importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Native.nativeWin,
                ],
            }]),
            ...addIfLazy(Context.getCurrentHost().os === "unix", () => [{
                subfolder: r`runtimes/linux-x64/native/`,
                contents: [
                    ...importFrom("BuildXL.Utilities").withQualifier(netStandardLinuxPackageQualifier).Native.nativeLinux,
                ],
            }])
        ]
    };

    const utilitiesCoreSpecification = {
        id: buildXLUtilitiesCoreIdentity,
        assemblies: [
            // BuildXL.Utilities.Core
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Utilities.Core.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Utilities.Core.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Utilities.Core.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Utilities.Core.dll,
        ],
        deploymentOptions: reducedDeploymentOptions,
    };

    const nativeSpecification = {
        id: buildXLNativeIdentity,
        assemblies: [
            // BuildXL.Native
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(net7PackageQualifier).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Native.dll,
        ],
        dependencies: [
            // This package references BuildXL.Tracing, which means that it requires a dependency on BuildXL.Utilities
            // However, it's not possible to add a dependency on BuildXL.Utilities because of the way this package is consumed by downstream consumers.
            buildXLUtilitiesCoreIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions,
    };

    const pipsSpecification = {
        id: buildXLPipsIdentity,
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
            // This package still uses the old cache packages, so inferInternalDependencies is set to false
            buildXLUtilitiesIdentity,
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,

            buildXLCacheHashingIdentity,
            buildXLCacheInterfacesIdentity,

            // TODO: Update to use new cache packages to replace the ones above once consumers of this package switch over
            // buildXLContentStoreHashingIdentity,
            // buildXLContentStoreUtilitiesCoreIdentity,
            // buildXLContentStoreInterfacesIdentity,
            // buildXLMemoizationStoreInterfacesIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions,
    };

    const processesSpecification = {
        id: { id: `${packageNamePrefix}.Processes`, version: Branding.Nuget.packageVersion },
        assemblies: [
            // BuildXL.Processes
            importFrom("BuildXL.Engine").withQualifier(net472packageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net6PackageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net7PackageQualifier).Processes.dll,
        ],
        dependencies: [
            // This package references BuildXL.Tracing, which means that it requires a dependency on BuildXL.Utilities
            // However, it's not possible to add a dependency on BuildXL.Utilities because of the way this package is consumed by downstream consumers.
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions
    };

    const processesLinuxSpecification = {
        id: { id: `${packageNamePrefix}.Processes.linux-x64`, version: Branding.Nuget.packageVersion },
        assemblies: [
            importFrom("BuildXL.Engine").withQualifier(net6LinuxPackageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net7LinuxPackageQualifier).Processes.dll
        ],
        dependencies: [
            // This package references BuildXL.Tracing, which means that it requires a dependency on BuildXL.Utilities
            // However, it's not possible to add a dependency on BuildXL.Utilities because of the way this package is consumed by downstream consumers.
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions
    };

    const engineCacheSpecification = {
        id: { id: `${packageNamePrefix}.Engine.Cache`, version: Branding.Nuget.packageVersion },
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
            // This package still uses the old cache packages, so inferInternalDependencies is set to false
            buildXLUtilitiesIdentity,
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,

            buildXLCacheHashingIdentity,
            buildXLCacheInterfacesIdentity,
            buildXLCacheLibrariesIdentity,
            
            // TODO: Update to use new cache packages to replace the ones above once consumers of this package switch over
            // buildXLContentStoreHashingIdentity,
            // buildXLContentStoreUtilitiesCoreIdentity,
            
            // buildXLContentStoreInterfacesIdentity,
            // buildXLMemoizationStoreInterfacesIdentity,

            // buildXLContentStoreDistributedIdentity,
            // buildXLContentStoreLibraryIdentity,
            // buildXLContentStoreGrpcIdentity,
            // buildXLContentStoreVstsIdentity,
            // buildXLMemoizationStoreDistributedIdentity,
            // buildXLMemoizationStoreLibraryIdentity,
            // ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [ 
            //     buildXLMemoizationStoreVstsIdentity,
            //     buildXLContentStoreVstsInterfacesIdentity
            // ]),
            // buildXLMemoizationStoreVstsInterfacesIdentity,
            // buildXLCacheHostServicesIdentity,
            // buildXLCacheHostConfigurationIdentity,
            // buildXLCacheLoggingIdentity
        ]
    };

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

    // Old cache packages that will be replaced in the future with the ones below them that contain a single assembly per package
    // These are maintained for compatibility
    const cacheLibraries = !canBuildAllPackagesOnThisHost ? undefined : pack({
        id: buildXLCacheLibrariesIdentity.id,
        deployment: Cache.NugetPackages.libraries,
        dependencies: [
            buildXLCacheInterfacesIdentity,
            buildXLUtilitiesIdentity,
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,

            importFrom("Azure.Messaging.EventHubs").withQualifier(net472packageQualifier).pkg,
            importFrom("Azure.Core.Amqp").withQualifier(net472packageQualifier).pkg,
            importFrom("Azure.Identity").withQualifier(net472packageQualifier).pkg,
            importFrom("Microsoft.Azure.Amqp").withQualifier(net472packageQualifier).pkg,
            importFrom("System.Threading.Tasks.Dataflow").withQualifier(net472packageQualifier).pkg,
            ...BuildXLSdk.withQualifier(net472packageQualifier).bclAsyncPackages,
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net472packageQualifier).getGrpcPackagesWithoutNetStandard(),
            ...importFrom("BuildXL.Cache.ContentStore").withQualifier(net6PackageQualifier).getGrpcDotNetPackages(),
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
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,

            importFrom("System.Threading.Tasks.Dataflow").withQualifier(net472packageQualifier).pkg,
            ...BuildXLSdk.withQualifier(net472packageQualifier).bclAsyncPackages,
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

    // New cache packages that contain a single assembly per package
    // NOTE: Only dependencies on other BuildXL packages need to be declared as dependencies here.
    // External dependencies will be inferred from the references made by the assemblies in this package. 
    // BuildXL.ContentStore.Distributed
    const cacheContentStoreDistributedSpecification = {
        id: buildXLContentStoreDistributedIdentity,
        assemblies: [ ...Cache.NugetPackages.contentStoreDistributed ],
    };

    // BuildXL.ContentStore.Library
    const cacheContentStoreLibrarySpecification = {
        id: buildXLContentStoreLibraryIdentity,
        assemblies: [ ...Cache.NugetPackages.contentStoreLibrary ]
    };

    // BuildXL.ContentStore.Grpc
    const cacheContentStoreGrpcSpecification = {
        id: buildXLContentStoreGrpcIdentity,
        assemblies: [ ...Cache.NugetPackages.contentStoreGrpc ]
    };

    // BuildXL.ContentStore.Vsts
    const cacheContentStoreVstsSpecification = {
        id: buildXLContentStoreVstsIdentity,
        assemblies: [ ...Cache.NugetPackages.contentStoreVsts ],
    };

    // BuildXL.ContentStore.VstsInterfaces
    const cacheContentStoreVstsInterfacesSpecification = {
        id: buildXLContentStoreVstsInterfacesIdentity,
        assemblies: [ ...Cache.NugetPackages.contentStoreVstsInterfaces ],
    };

    // BuildXL.MemoizationStore.Distributed
    const cacheMemoizationStoreDistributedSpecification = {
        id: buildXLMemoizationStoreDistributedIdentity,
        assemblies: [ ...Cache.NugetPackages.memoizationStoreDistributed ],
    };

    // BuildXL.MemoizationStore.Library
    const cacheMemoizationStoreLibrarySpecification = {
        id: buildXLMemoizationStoreLibraryIdentity,
        assemblies: [ ...Cache.NugetPackages.memoizationStoreLibrary ],
    };

    // BuildXL.MemoizationStore.Vsts
    const cacheMemoizationStoreVstsSpecification = {
        id: buildXLMemoizationStoreVstsIdentity,
        assemblies: [ ...Cache.NugetPackages.memoizationStoreVsts ],
    };

    // BuildXL.MemoizationStore.VstsInterfaces
    const cacheMemoizationStoreVstsInterfacesSpecification = {
        id: buildXLMemoizationStoreVstsInterfacesIdentity,
        assemblies: [ ...Cache.NugetPackages.memoizationStoreVstsInterfaces ],
    };

    // BuildXL.Cache.Host.Service
    const cacheHostServicesSpecification = {
        id: buildXLCacheHostServicesIdentity,
        assemblies: [ ...Cache.NugetPackages.buildxlCacheHostServices ],
    };

    // BuildXL.Cache.Host.Configuration
    const cacheHostConfigurationSpecification = {
        id: buildXLCacheHostConfigurationIdentity,
        assemblies: [ ...Cache.NugetPackages.buildxlCacheHostConfiguration ],
    };

    // BuildXL.Cache.Logging
    const cacheLoggingSpecification = {
        id: buildXLCacheLoggingIdentity,
        assemblies: [ ...Cache.NugetPackages.buildxlCacheLogging ],
    };

    // BuildXL.ContentStore.Interfaces
    const cacheContentStoreInterfacesSpecification = {
        id: buildXLContentStoreInterfacesIdentity,
        assemblies: [ ...Cache.NugetPackages.contentStoreInterfaces ],
    };

    // BuildXL.MemoizationStore.Interfaces
    const cacheMemoizationStoreInterfacesSpecification = {
        id: buildXLMemoizationStoreInterfacesIdentity,
        assemblies: [ ...Cache.NugetPackages.memoizationStoreInterfaces ],
    };

    // BuildXL.ContentStore.Hashing
    const cacheContentStoreHashingSpecification = {
        id: buildXLContentStoreHashingIdentity,
        assemblies: [ ...Cache.NugetPackages.contentStoreHashing ],
    };

    // BuildXL.ContentStore.UtilitiesCore
    const cacheContentStoreUtilitiesCoreSpecification = {
        id: buildXLContentStoreUtilitiesCoreIdentity,
        assemblies: [ ...Cache.NugetPackages.contentStoreUtilitiesCore ],
    };

    // BuildXL.Cache.BlobLifetimeManager.Library
    const blobLifetimeManagerLibrarySpecification = {
        id: buildXLBlobLifetimeManagerIdentity,
        assemblies: [ ...Cache.NugetPackages.blobLifetimeManagerLibrary ],
    };

    /**
     * A set of all package specifications built by BuildXL.
     * When adding a new package, add its package specification here.
     */
    const packageSpecifications : Nuget.PackageSpecification[] = [
        ariaCommonSpecification,
        nativeSpecification,
        utilitiesCoreSpecification,
        utilitiesSpecification,
        cacheContentStoreDistributedSpecification,
        cacheContentStoreLibrarySpecification,
        cacheContentStoreGrpcSpecification,
        ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [ 
            cacheContentStoreVstsSpecification,
            cacheContentStoreVstsInterfacesSpecification,
            cacheMemoizationStoreVstsSpecification,
            cacheMemoizationStoreVstsInterfacesSpecification
        ]),
        cacheMemoizationStoreDistributedSpecification,
        cacheMemoizationStoreLibrarySpecification,
        cacheHostServicesSpecification,
        cacheHostConfigurationSpecification,
        cacheLoggingSpecification,
        cacheContentStoreInterfacesSpecification,
        cacheMemoizationStoreInterfacesSpecification,
        cacheContentStoreHashingSpecification,
        cacheContentStoreUtilitiesCoreSpecification,
        blobLifetimeManagerLibrarySpecification,
    ];

    const packageBranding : Nuget.PackageBranding = {
        company: Branding.company,
        shortProductName: Branding.shortProductName,
        version:Branding.Nuget.packageVersion,
        authors: Branding.Nuget.packageAuthors,
        owners:Branding.Nuget.packageOwners,
        copyright: Branding.Nuget.pacakgeCopyright
    };

    /**
     * Several of the packages below have inferInternalDependencies set to false on purpose.
     * Many of these are older BuildXL nuget packages that have always had their dependencies incorrectly specified.
     * Fixing them requires more work that will come in the future change.
     * Changing them now would result in downstream consumers needing to include dependencies they may not necessarily want to add. 
     * For example, BuildX.Processes will have a dependency on BuildXL.Utilties which is undesirable because Utilities contains a lot of external dependencies.
     */
    const ariaCommon = Nuget.packAssembliesAndAssertDependencies(ariaCommonSpecification, packageSpecifications, packageBranding, /** inferInternalDependencies */ true, /* dependencyScope */ []);
    const utilities = Nuget.packAssemblies(utilitiesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ false);
    // NOTE: utilitiesCore, native, processes, and processesLinux have a restricted set of dependencies.
    // Do not modify its set of allowed dependencies without first consulting with the BuildXL team.
    const utilitiesCore = Nuget.packAssembliesAndAssertDependencies(utilitiesCoreSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true, /* dependencyScope */ []);
    const native = Nuget.packAssembliesAndAssertDependencies(nativeSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true,/* dependencyScope */ [buildXLUtilitiesCoreIdentity]);
    const pips = Nuget.packAssemblies(pipsSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ false);
    const processes = Nuget.packAssembliesAndAssertDependencies(processesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true, /* dependencyScope */ [buildXLUtilitiesCoreIdentity, buildXLNativeIdentity]);
    const processesLinux = Nuget.packAssembliesAndAssertDependencies(processesLinuxSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ false, /* dependencyScope */ [buildXLUtilitiesCoreIdentity, buildXLNativeIdentity]);
    const engineCache = Nuget.packAssemblies(engineCacheSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ false);
    const cacheContentStoreDistributed = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheContentStoreDistributedSpecification, packageSpecifications, packageBranding, /* inferBuildXLDepencies */ true);
    const cacheContentStoreLibrary = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheContentStoreLibrarySpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreGrpc = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheContentStoreGrpcSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreVsts = !canBuildAllPackagesOnThisHost || !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : Nuget.packAssemblies(cacheContentStoreVstsSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreVstsInterfaces = !canBuildAllPackagesOnThisHost || !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : Nuget.packAssemblies(cacheContentStoreVstsInterfacesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreDistributed = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheMemoizationStoreDistributedSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreLibrary = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheMemoizationStoreLibrarySpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreVsts = !canBuildAllPackagesOnThisHost || !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : Nuget.packAssemblies(cacheMemoizationStoreVstsSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreVstsInterfaces = !canBuildAllPackagesOnThisHost || !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : Nuget.packAssemblies(cacheMemoizationStoreVstsInterfacesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheHostServices = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheHostServicesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheHostConfiguration = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheHostConfigurationSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheLogging = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheLoggingSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreInterfaces = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheContentStoreInterfacesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreInterfaces = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheMemoizationStoreInterfacesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreHashing = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheContentStoreHashingSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreUtilitiesCore = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(cacheContentStoreUtilitiesCoreSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const blobLifetimeManagerLibrary = !canBuildAllPackagesOnThisHost ? undefined : Nuget.packAssemblies(blobLifetimeManagerLibrarySpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);

    const cacheLibrariesPackages = [
        cacheContentStoreDistributed,
        cacheContentStoreLibrary,
        cacheContentStoreGrpc,
        cacheContentStoreVsts,
        cacheContentStoreVstsInterfaces,
        cacheMemoizationStoreDistributed,
        cacheMemoizationStoreLibrary,
        cacheMemoizationStoreVsts,
        cacheMemoizationStoreVstsInterfaces,
        cacheHostServices,
        cacheHostConfiguration,
        cacheLogging
    ];

    const cacheInterfacesPackages = [
        cacheContentStoreInterfaces,
        cacheMemoizationStoreInterfaces
    ];

    const cacheHashingPackages = [
        cacheContentStoreHashing,
        cacheContentStoreUtilitiesCore
    ];
    // End cache packages

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
                    winX64, winX64Net7
                ),
                cacheTools,
                cacheLibraries,
                ...cacheLibrariesPackages,
                cacheInterfaces,
                ...cacheInterfacesPackages,
                cacheService,
                cacheHashing,
                ...cacheHashingPackages,
                blobLifetimeManagerLibrary,
                ariaCommon,
                utilities,
                utilitiesCore,
                native,
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
                linuxX64,
                linuxX64Net7,
                processesLinux
            ]),
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
            metadata:  Nuget.createMetaData({id: args.id, dependencies: dependencies,copyContentFiles: args.copyContentFiles, packageBranding: packageBranding}),
            deployment: args.deployment,
            deploymentOptions: args.deploymentOptions,
            noPackageAnalysis: true,
            noDefaultExcludes: true,
            filterFiles: args.filterFiles,
        }).nuPkg;
    }

    export function isManagedPackage(item: Nuget.Dependency | Managed.ManagedNugetPackage) : item is Managed.ManagedNugetPackage {
        return item["compile"] !== undefined || item["runtime"] !== undefined || item["runtimeContent"] !== undefined || item["analyzers"] !== undefined;
    }
}
