// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Branding from "BuildXL.Branding";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed.Shared";
import * as Nuget from "Sdk.BuildXL.Tools.NuGet";

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
     * 5. Call `packAssemblies` with inferInternalDependencies set to true to enable package dependency verification.
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

    const net8PackageQualifier = {
        targetFramework: "net8.0",
        targetRuntime: "win-x64"
    };

    const net9PackageQualifier = {
        targetFramework: "net9.0",
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
    
    const net8LinuxPackageQualifier = {
        targetFramework: "net8.0",
        targetRuntime: "linux-x64"
    };

    const net9LinuxPackageQualifier = {
        targetFramework: "net9.0",
        targetRuntime: "linux-x64"
    };

    const canBuildAllPackagesOnThisHost = Context.getCurrentHost().os === "win";

    const packageNamePrefix = BuildXLSdk.Flags.isMicrosoftInternal
        ? "BuildXL"
        : "Microsoft.BuildXL";

    /** 
     * The notice file compiles the license and copyright information for any code or other materials under open source licenses that we distribute in a Microsoft Offering. 
     * The notice file is automatically generated in our rolling builds before we execute the selfhost build that produce the nuget packages.
     * In those rolling builds, the notice file is put on the source root.
    */
    const includeNoticeFile = !BuildXLSdk.Flags.isMicrosoftInternal;
    const noticeFilePath = f`${Context.getMount("SourceRoot").path}/NOTICE.txt`;

    const buildXLAriaCommonIdentity = { id: `${packageNamePrefix}.AriaCommon`, version: Branding.Nuget.packageVersion };
    const buildXLUtilitiesIdentity = { id: `${packageNamePrefix}.Utilities`, version: Branding.Nuget.packageVersion };
    const buildXLUtilitiesCoreIdentity = { id: `${packageNamePrefix}.Utilities.Core`, version: Branding.Nuget.packageVersion };
    const buildXLNativeIdentity = { id: `${packageNamePrefix}.Native`, version: Branding.Nuget.packageVersion };
    const buildXLPipsIdentity = { id: `${packageNamePrefix}.Pips`, version: Branding.Nuget.packageVersion };

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
    const buildXLBuildCacheResourceHelperIdentity = { id: `${packageNamePrefix}.Cache.BuildCacheResource.Helper`, version: Branding.Nuget.packageVersion };

    // External packages
    // The macOS runtime package is only produced publicly, so 'Microsoft.BuildXL' will always be its prefix (ie: instead of using `packageNamePrefix`)
    // To produce a new version of this package please refer to our internal BuildXL OneNote in the macOS section.
    const buildXLMacOSRuntimeIdentity = { id: `Microsoft.BuildXL.Interop.Runtime.osx-x64`, version: `20230818.1.0` };

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

    // BuildXL.AriaCommon
    const ariaCommonSpecification : Nuget.PackageSpecification = {
        id: buildXLAriaCommonIdentity,
        assemblies: [
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(net472packageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(net6PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(net8PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(net9PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.withQualifier(netstandard20PackageQualifier).dll,
        ]
    };

    const utilitiesSpecification : Nuget.PackageSpecification = {
        id: buildXLUtilitiesIdentity,
        assemblies: [
            // BuildXL.Utilities
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).dll,
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).dll,

            // BuildXL.Utilities.Branding
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Branding.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Branding.dll,

            // BuildXL.KeyValueStore
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).KeyValueStore.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).KeyValueStore.dll,

            // BuildXL.Native.Extensions
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Native.Extensions.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Native.Extensions.dll,
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Native.Extensions.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Native.Extensions.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Native.Extensions.dll,

            // BuildXL.Configuration
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Configuration.dll,
            importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Configuration.dll,

            // BuildXL.SBOMUtilities
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, 
                importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).SBOMUtilities.dll
            ),

            // BuildXL.Instrumentation.Tracing
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net472packageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net6PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net8PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(net9PackageQualifier).dll,
            importFrom("BuildXL.Utilities.Instrumentation").Tracing.withQualifier(netstandard20PackageQualifier).dll,

            // BuildXL.Utilities.Authentication
            ...addIf(BuildXLSdk.Flags.isMicrosoftInternal, 
                importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(netstandard20PackageQualifier).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Authentication.dll,
                importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Authentication.dll
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
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Utilities.Core.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Utilities.Core.dll,
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
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Native.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Native.dll,
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
            importFrom("BuildXL.Pips").withQualifier(net8PackageQualifier).dll,
            importFrom("BuildXL.Pips").withQualifier(net9PackageQualifier).dll,

            // BuildXL.Ipc
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Ipc.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Ipc.dll,
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Ipc.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Ipc.dll,

            // BuildXL.Storage
            importFrom("BuildXL.Utilities").withQualifier(net472packageQualifier).Storage.dll,
            importFrom("BuildXL.Utilities").withQualifier(net6PackageQualifier).Storage.dll,
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Storage.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Storage.dll,
        ],
        dependencies: [
            // This package still uses the old cache packages, so inferInternalDependencies is set to false
            buildXLUtilitiesIdentity,
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,

            buildXLContentStoreHashingIdentity,
            buildXLContentStoreUtilitiesCoreIdentity,
            buildXLContentStoreInterfacesIdentity,
            buildXLMemoizationStoreInterfacesIdentity,
        ],
        deploymentOptions: reducedDeploymentOptions,
    };

    const processesSpecification = {
        id: { id: `${packageNamePrefix}.Processes`, version: Branding.Nuget.packageVersion },
        assemblies: [
            // BuildXL.Processes
            importFrom("BuildXL.Engine").withQualifier(net472packageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net6PackageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net8PackageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net9PackageQualifier).Processes.dll,
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
            importFrom("BuildXL.Engine").withQualifier(net8LinuxPackageQualifier).Processes.dll,
            importFrom("BuildXL.Engine").withQualifier(net9LinuxPackageQualifier).Processes.dll,
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

            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net8PackageQualifier).InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net8PackageQualifier).Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net8PackageQualifier).BasicFilesystem.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net8PackageQualifier).BuildCacheAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net8PackageQualifier).MemoizationStoreAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net8PackageQualifier).VerticalAggregator.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net8PackageQualifier).ImplementationSupport.dll,
            importFrom("BuildXL.Utilities").withQualifier(net8PackageQualifier).Storage.dll,

            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net9PackageQualifier).InMemory.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net9PackageQualifier).Interfaces.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net9PackageQualifier).BasicFilesystem.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net9PackageQualifier).BuildCacheAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net9PackageQualifier).MemoizationStoreAdapter.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net9PackageQualifier).VerticalAggregator.dll,
            importFrom("BuildXL.Cache.VerticalStore").withQualifier(net9PackageQualifier).ImplementationSupport.dll,
            importFrom("BuildXL.Utilities").withQualifier(net9PackageQualifier).Storage.dll,
        ],
        dependencies: [
            // This package still uses the old cache packages, so inferInternalDependencies is set to false
            buildXLUtilitiesIdentity,
            buildXLUtilitiesCoreIdentity,
            buildXLNativeIdentity,

            buildXLContentStoreHashingIdentity,
            buildXLContentStoreUtilitiesCoreIdentity,

            buildXLContentStoreInterfacesIdentity,
            buildXLMemoizationStoreInterfacesIdentity,

            buildXLContentStoreDistributedIdentity,
            buildXLContentStoreLibraryIdentity,
            buildXLContentStoreGrpcIdentity,
            buildXLContentStoreVstsIdentity,
            buildXLMemoizationStoreDistributedIdentity,
            buildXLMemoizationStoreLibraryIdentity,
            ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [ 
                buildXLMemoizationStoreVstsIdentity,
                buildXLContentStoreVstsInterfacesIdentity
            ]),
            buildXLMemoizationStoreVstsInterfacesIdentity,
            buildXLCacheHostServicesIdentity,
            buildXLCacheHostConfigurationIdentity,
            buildXLCacheLoggingIdentity
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
                        contents: [importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net6.0", targetRuntime: "win-x64" }).LauncherServer.exe]
                    },
                    {
                        subfolder: r`net8.0`,
                        contents: [importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net8.0", targetRuntime: "win-x64" }).LauncherServer.exe]
                    },
                    {
                        subfolder: r`net9.0`,
                        contents: [importFrom("BuildXL.Cache.DistributedCache.Host").withQualifier({ targetFramework: "net9.0", targetRuntime: "win-x64" }).LauncherServer.exe]
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

    // BuildXL.Cache.BlobLifetimeManager.Library
    const buildCacheResourceHelperSpecification = {
        id: buildXLBuildCacheResourceHelperIdentity,
        assemblies: [ ...Cache.NugetPackages.buildCacheResourceHelper ],
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
        cacheContentStoreVstsInterfacesSpecification,
        cacheMemoizationStoreVstsInterfacesSpecification,
        ...addIfLazy(BuildXLSdk.Flags.isVstsArtifactsEnabled, () => [ 
            cacheContentStoreVstsSpecification,
            cacheMemoizationStoreVstsSpecification,
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
        buildCacheResourceHelperSpecification
    ];

    const packageBranding : Nuget.PackageBranding = {
        company: Branding.company,
        shortProductName: Branding.shortProductName,
        version:Branding.Nuget.packageVersion,
        authors: Branding.Nuget.packageAuthors,
        owners:Branding.Nuget.packageOwners,
        copyright: Branding.Nuget.packageCopyright
    };

    /**
     * Several of the packages below have inferInternalDependencies set to false on purpose.
     * Many of these are older BuildXL nuget packages that have always had their dependencies incorrectly specified.
     * Fixing them requires more work that will come in the future change.
     * Changing them now would result in downstream consumers needing to include dependencies they may not necessarily want to add. 
     * For example, BuildX.Processes will have a dependency on BuildXL.Utilties which is undesirable because Utilities contains a lot of external dependencies.
     */
    const ariaCommon = packAssembliesAndAssertDependencies(ariaCommonSpecification, packageSpecifications, packageBranding, /** inferInternalDependencies */ true, /* dependencyScope */ []);
    const utilities = Nuget.packAssemblies(utilitiesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ false);
    // NOTE: utilitiesCore, native, processes, and processesLinux have a restricted set of dependencies.
    // Do not modify its set of allowed dependencies without first consulting with the BuildXL team.
    const utilitiesCore = packAssembliesAndAssertDependencies(utilitiesCoreSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true, /* dependencyScope */ []);
    const native = packAssembliesAndAssertDependencies(nativeSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true,/* dependencyScope */ [buildXLUtilitiesCoreIdentity]);
    const pips = packAssemblies(pipsSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ false);
    const processes = packAssembliesAndAssertDependencies(processesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true, /* dependencyScope */ [buildXLUtilitiesCoreIdentity, buildXLNativeIdentity]);
    const processesLinux = packAssembliesAndAssertDependencies(processesLinuxSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ false, /* dependencyScope */ [buildXLUtilitiesCoreIdentity, buildXLNativeIdentity]);
    const engineCache = packAssemblies(engineCacheSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ false);
    const cacheContentStoreDistributed = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheContentStoreDistributedSpecification, packageSpecifications, packageBranding, /* inferBuildXLDepencies */ true);
    const cacheContentStoreLibrary = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheContentStoreLibrarySpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreGrpc = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheContentStoreGrpcSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreVsts = !canBuildAllPackagesOnThisHost || !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : packAssemblies(cacheContentStoreVstsSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreVstsInterfaces = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheContentStoreVstsInterfacesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreDistributed = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheMemoizationStoreDistributedSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreLibrary = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheMemoizationStoreLibrarySpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreVsts = !canBuildAllPackagesOnThisHost || !BuildXLSdk.Flags.isVstsArtifactsEnabled ? undefined : packAssemblies(cacheMemoizationStoreVstsSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreVstsInterfaces = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheMemoizationStoreVstsInterfacesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheHostServices = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheHostServicesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheHostConfiguration = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheHostConfigurationSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheLogging = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheLoggingSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreInterfaces = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheContentStoreInterfacesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheMemoizationStoreInterfaces = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheMemoizationStoreInterfacesSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreHashing = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheContentStoreHashingSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const cacheContentStoreUtilitiesCore = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(cacheContentStoreUtilitiesCoreSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const blobLifetimeManagerLibrary = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(blobLifetimeManagerLibrarySpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);
    const buildCacheResourceHelper = !canBuildAllPackagesOnThisHost ? undefined : packAssemblies(buildCacheResourceHelperSpecification, packageSpecifications, packageBranding, /* inferInternalDependencies */ true);

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
    const toolsAdoBuildRunner = pack({
        id: `${packageNamePrefix}.Tools.AdoBuildRunner.osx-x64`,
        deployment: importFrom("BuildXL.AdoBuildRunner").BuildXL.AdoBuildRunner.withQualifier({
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
                ...cacheLibrariesPackages,
                ...cacheInterfacesPackages,
                cacheService,
                ...cacheHashingPackages,
                blobLifetimeManagerLibrary,
                buildCacheResourceHelper,
                ariaCommon,
                utilities,
                utilitiesCore,
                native,
                pips,
                processes,
                engineCache,
                sdks,
                osxX64,
                toolsAdoBuildRunner,
            ]),
            ...addIfLazy(!BuildXLSdk.Flags.genVSSolution && Context.getCurrentHost().os === "unix", () => [
                linuxX64,
                processesLinux
            ]),
        ]
    };

    @@public
    export const deployed = BuildXLSdk.DeploymentHelpers.deploy({
        definition: deployment,
        targetLocation: packageTargetFolder,
    });

    export interface PackArgs {
        id: string,
        deployment: Deployment.Definition,
        deploymentOptions?: Managed.Deployment.FlattenOptions,
        copyContentFiles?: boolean,
        dependencies?: (Nuget.Dependency | Managed.ManagedNugetPackage)[],
        filterFiles?: PathAtom[]
    }

    export function pack(args: PackArgs) : File {

        if (includeNoticeFile) {
            args = args.override<PackArgs>({
                deployment: <Deployment.Definition>{
                    contents: [
                        ...(args.deployment.contents || []),
                        noticeFilePath,
                    ]
                }
            });
        }
        const dependencies : Nuget.Dependency[] = (args.dependencies || [])
            .map(dep => {
                if (isManagedPackage(dep)) {
                    return {id: dep.name, version: dep.version};
                } else {
                    return dep;
                }
            });

        return Nuget.pack({
            metadata:  Nuget.createMetaData({id: args.id, dependencies: dependencies, copyContentFiles: args.copyContentFiles, packageBranding: packageBranding}),
            deployment: args.deployment,
            deploymentOptions: args.deploymentOptions,
            noPackageAnalysis: true,
            noDefaultExcludes: true,
            filterFiles: args.filterFiles,
        }).nuPkg;
    }


    function addNoticeFileIfNeeded(args: Nuget.PackageSpecification) : Nuget.PackageSpecification
    {
        if (!includeNoticeFile) {
            return args;
        }

        return args.override<Nuget.PackageSpecification>({
            additionalContent: [
                ...(args.additionalContent || []),
                noticeFilePath,
            ],
        });
    }

    function packAssemblies(
        args: Nuget.PackageSpecification,
        packageSpecifications : Nuget.PackageSpecification[],
        packageBranding : Nuget.PackageBranding,
        inferInternalDependencies : boolean) : File
    {
        return Nuget.packAssemblies(addNoticeFileIfNeeded(args), packageSpecifications, packageBranding, inferInternalDependencies);
    }

    function packAssembliesAndAssertDependencies(
        args: Nuget.PackageSpecification,
        packageSpecifications : Nuget.PackageSpecification[],
        packageBranding : Nuget.PackageBranding,
        inferInternalDependencies : boolean,
        allowedDependecies : Nuget.PackageIdentifier[] ) : File
    {
        return Nuget.packAssembliesAndAssertDependencies(addNoticeFileIfNeeded(args), packageSpecifications, packageBranding, inferInternalDependencies, allowedDependecies);
    }

    export function isManagedPackage(item: Nuget.Dependency | Managed.ManagedNugetPackage) : item is Managed.ManagedNugetPackage {
        return item["compile"] !== undefined || item["runtime"] !== undefined || item["runtimeContent"] !== undefined || item["analyzers"] !== undefined;
    }
}
