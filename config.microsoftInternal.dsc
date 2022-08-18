// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const isMicrosoftInternal = Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

const artifactNugetVersion = "19.208.32712-buildid17682333";
const azureDevopsNugetVersion = "19.208.0-internal202207122";

// These packages are Microsoft internal packages.
// These consist of internally repackaged products that we can't push to a public feed and have to rely on users installing locally.
// Or they contain code which is internal and can't be open sourced due to tying into Microsoft internal systems.
// The dependent code is still open sourced, but not compiled in the public repo.
export const pkgs = isMicrosoftInternal ? [
    { id: "BuildXL.DeviceMap", version: "0.0.1" },

    // Metrics library used by .net core CaSaaS
    {id: "Microsoft.Cloud.InstrumentationFramework", version: "3.3.8.1"},

    // Runtime dependencies used for macOS deployments
    { id: "runtime.osx-x64.BuildXL", version: "3.8.99" },
    { id: "Aria.Cpp.SDK.win-x64", version: "8.5.6", osSkip: [ "macOS", "unix" ] },
    // cross-plat Aria SDK and its dependencies
    { id: "Microsoft.Applications.Events.Server", version: "1.1.3.308", dependentPackageIdsToIgnore: [ "Microsoft.Data.SQLite" ] },
    { id: "Microsoft.Data.Sqlite", version: "1.1.1" },
    { id: "SQLite", version: "3.13.0" },

    { id: "CB.QTest", version: "22.7.7.94259", osSkip: [ "macOS", "unix" ] },

    { id: "BuildXL.Tracing.AriaTenantToken", version: "1.0.0" },

    // Windows Sdk so microsoft dev's don't have to install it.
    { id: "WindowsSdk.Corext", version: "10.0.16299.1", alias: "Windows.Sdk", osSkip: [ "macOS", "unix" ] },

    // Artifact packages and dependencies
    { id: "Microsoft.VisualStudio.Services.ArtifactServices.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "Microsoft.Azure.Cosmos.Table", "Microsoft.Azure.Storage.Blob", "BuildXL.Utilities", "DotNetFxRefAssemblies.Corext", "Mono.Unix" ] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.VisualStudio.Services.Client", version: azureDevopsNugetVersion, dependentPackageIdsToSkip: [ "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "System.Security.Cryptography.OpenSsl", "System.Security.Principal.Windows" ] },
    { id: "Microsoft.VisualStudio.Services.InteractiveClient", version: azureDevopsNugetVersion, dependentPackageIdsToSkip: [ "Ben.Demystifier" ], dependentPackageIdsToIgnore: [ "Ben.Demystifier" ] },
    { id: "Microsoft.Azure.Storage.Common", version:"11.2.2" },

    { id: "GVFS.Installers", version: "0.3.20147.1" },

    // DropDaemon Artifact dependencies.
    // Here, even though the packages depend on Cache bits other than Hashing, we make sure that the codepaths that actually depend on them are never activated. This is to ensure that there is no cyclic dependency between BXL and AzureDevOps.
    // This is further enforced by not including Cache bits in DropDaemon, other than BuildXL.Cache.Hashing.
    { id: "ArtifactServices.App.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "DotNetFxRefAssemblies.Corext"] },
    { id: "ArtifactServices.App.Shared.Cache", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.Utilities", "DotNetFxRefAssemblies.Corext", "System.Data.SQLite.Core"] },
    { id: "Drop.App.Core", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.Utilities", "DotNetFxRefAssemblies.Corext", "System.Data.SQLite.Core"] },
    { id: "Drop.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "DotNetFxRefAssemblies.Corext"] },
    { id: "ItemStore.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client.Cache", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.Utilities", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.Windows.Debuggers.SymstoreInterop", version: "1.0.1-netstandard2.0" },
    { id: "Symbol.App.Core", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.Utilities", "DotNetFxRefAssemblies.Corext"] },
    { id: "Symbol.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "DotNetFxRefAssemblies.Corext"] },
    { id: "TransientFaultHandling.Core", version: "5.1.1209.1" },

    // CloudTest internal dependencies
    { id: "GvfsTestHelpersForBuildXL", version: "0.1.0"},

    // Internal pacakged version to avoid downloading from the web but the trusted stable internal feed:
    { id: "PowerShell.Core", version: "6.1.0", osSkip: [ "macOS", "unix" ] },

    // IcM and dependencies
    { id: "Microsoft.AzureAd.Icm.Types.amd64", version: "2.2.1363.11" },
    { id: "Microsoft.AzureAd.Icm.WebService.Client.amd64", version: "2.2.1363.11" },

    // Internal version of Redis
    { id: "Microsoft.Caching.Redis", version: "3.0.57",
        dependentPackageIdsToSkip: [ "System.Runtime.CompilerServices.Unsafe", "System.IO.Pipelines", "System.Threading.Channels", "Pipelines.Sockets.Unofficial" ] },

    // Cpp Sdk
    { id: "VisualCppTools.Internal.VS2017Layout", version: "14.16.27034", osSkip: [ "macOS", "unix" ] },

    // SBOM Generation
    { id: "Microsoft.SBOMCore", version: "2.1.35", dependentPackageIdsToSkip: ["Microsoft.Extensions.Logging.Abstractions"] },
    { id: "Microsoft.Parsers.ManifestGenerator", version: "2.1.35", dependentPackageIdsToSkip: ["Newtonsoft.Json"]},
    { id: "Microsoft.Parsers.SPDX22SBOMParser", version: "2.1.35" },
    { id: "Microsoft.SBOM.Adapters", version: "2.1.35", dependentPackageIdsToSkip : ["Newtonsoft.Json", "PackageUrl"] },
    { id: "Microsoft.ComponentDetection.Contracts", version: "1.2.3", dependentPackageIdsToSkip: ["Newtonsoft.Json"] },
    { id: "Microsoft.ManifestInterface", version: "2.1.35", dependentPackageIdsToSkip: ["System.Text.Json"] },
    { id: "Microsoft.Sbom.Contracts", version: "2.1.35" },
    { id: "Microsoft.Bcl.HashCode", version: "1.1.1" },

    // Process remoting
    { id: "AnyBuild.SDK", version: "0.1.14" },

    // Part of VSSDK used by IDE/VsIntegration
    { id: "Microsoft.Internal.VisualStudio.Interop", version: "17.2.32405.191" },
    { id: "Microsoft.VisualStudio.ProjectSystem", version: "17.3.74-pre" },

    // RoslynAnalyzers internal analyzers
    { id: "Microsoft.Internal.Analyzers", version: "2.6.11"},
] : [

    // Artifact packages and dependencies in OSS
    { id: "Microsoft.VisualStudio.Services.Client", version: "16.148.0-preview", dependentPackageIdsToSkip: [ "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "System.Security.Cryptography.OpenSsl", "System.Security.Principal.Windows" ] },

];

// This contains facade modules for the packages that are only available internally
export const resolver = {
    kind: "SourceResolver",
    modules: [
        f`Private/InternalSdk/BuildXL.DeviceMap/module.config.dsc`,
        f`Private/InternalSdk/CB.QTest/module.config.dsc`,
        ...addIf(isMicrosoftInternal,
            f`Private/InternalSdk/PowerShell.Core/module.config.dsc`,
            f`Private/InternalSdk/Windows.Sdk/module.config.dsc`,
            f`Private/InternalSdk/InstrumentationFramework/module.config.dsc`
        ),

        f`Private/InternalSdk/Drop/module.config.dsc`,
        f`Private/InternalSdk/BuildXL.Tracing.AriaTenantToken/module.config.dsc`,
        f`Private/InternalSdk/AnyBuild.SDK/module.config.dsc`,
        f`Private/InternalSdk/Microsoft.Internal.VisualStudio.Interop/module.config.dsc`,
        f`Private/InternalSdk/Microsoft.VisualStudio.ProjectSystem/module.config.dsc`,
    ]
};
