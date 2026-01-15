// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const isMicrosoftInternal = Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

const artifactNugetVersion = "19.254.35907-buildid29691232";
const azureDevopsNugetVersion = "19.254.0-internal202503071";
const qtestNugetVersion = "25.8.28-gd566f35f";

// These packages are Microsoft internal packages.
// These consist of internally repackaged products that we can't push to a public feed and have to rely on users installing locally.
// Or they contain code which is internal and can't be open sourced due to tying into Microsoft internal systems.
// The dependent code is still open sourced, but not compiled in the public repo.
export const pkgs = isMicrosoftInternal ? [
    { id: "BuildXL.DeviceMap", version: "0.0.1" },

    // Runtime dependencies used for macOS deployments
    { id: "runtime.osx-x64.BuildXL", version: "3.8.99" },
    { id: "Aria.Cpp.SDK.win-x64", version: "8.5.6", osSkip: [ "macOS", "unix" ] },
    // cross-plat Aria SDK and its dependencies
    { id: "Microsoft.Applications.Events.Server", version: "1.1.3.308", dependentPackageIdsToIgnore: [ "Microsoft.Data.SQLite" ] },
    { id: "Microsoft.Data.Sqlite", version: "1.1.1" },
    { id: "SQLite", version: "3.13.0" },

    // Windows and Linux QTest packages are not aligned wrt versions. QTest folks will work on aligning them, but for the time being
    // these two may differ on the version number
    { id: "CB.QTest", version: qtestNugetVersion, osSkip: [ "macOS", "unix" ] },
    { id: "CB.QTestLinux", version: qtestNugetVersion, osSkip: [ "macOS"  ] },

    { id: "BuildXL.Tracing.AriaTenantToken", version: "1.0.0" },

    // Artifact packages and dependencies
    { id: "Microsoft.VisualStudio.Services.ArtifactServices.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: [
        "Microsoft.BuildXL.Cache.ContentStore.Hashing",
        "Microsoft.BuildXL.Cache.ContentStore.UtilitiesCore",
        "Microsoft.BuildXL.Cache.Hashing",
        "Microsoft.BuildXL.Cache.Interfaces",
        "Microsoft.Azure.Cosmos.Table",
        "Microsoft.Azure.Storage.Blob",
        "DotNetFxRefAssemblies.Corext",
        "Mono.Unix",
        "Microsoft.Identity.Client.Desktop" ] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.ContentStore.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.ContentStore.UtilitiesCore", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.VisualStudio.Services.Client", version: azureDevopsNugetVersion, dependentPackageIdsToSkip: [ "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "Microsoft.Data.SqlClient", "System.Security.Cryptography.OpenSsl", "System.Security.Principal.Windows" ] },
    { id: "Microsoft.VisualStudio.Services.InteractiveClient", version: azureDevopsNugetVersion, dependentPackageIdsToSkip: [ "Ben.Demystifier" ], dependentPackageIdsToIgnore: [ "Ben.Demystifier" ] },
    { id: "Microsoft.Azure.Storage.Common", version: "11.2.3" },
    { id: "System.ServiceProcess.ServiceController", version: "6.0.1" },
    { id: "Microsoft.TeamFoundationServer.Client", version: azureDevopsNugetVersion},
    { id: "Microsoft.TeamFoundation.DistributedTask.Common.Contracts", version: azureDevopsNugetVersion},

    // CloudStore dependencies
    { id: "DeduplicationSigned", version: "1.0.14" },

    // DropDaemon Artifact dependencies.
    // Here, even though the packages depend on Cache bits other than Hashing, we make sure that the codepaths that actually depend on them are never activated. This is to ensure that there is no cyclic dependency between BXL and AzureDevOps.
    // This is further enforced by not including Cache bits in DropDaemon, other than BuildXL.Cache.Hashing.
    { id: "ArtifactServices.App.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.ContentStore.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.ContentStore.Interfaces", "Microsoft.BuildXL.Cache.ContentStore.UtilitiesCore", "DotNetFxRefAssemblies.Corext"] },
    { id: "Drop.App.Core", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.Libraries", "Microsoft.BuildXL.Utilities", "Microsoft.BuildXL.Utilities.Core", "Microsoft.BuildXL.Native", "Microsoft.BuildXL.Cache.ContentStore.Hashing", "Microsoft.BuildXL.Cache.ContentStore.Interfaces", "Microsoft.BuildXL.Cache.ContentStore.UtilitiesCore", "DotNetFxRefAssemblies.Corext", "System.Data.SQLite.Core"] },
    { id: "Drop.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.ContentStore.Hashing", "Microsoft.BuildXL.Cache.ContentStore.UtilitiesCore", "DotNetFxRefAssemblies.Corext"] },
    { id: "ItemStore.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.ContentStore.Hashing", "Microsoft.BuildXL.Cache.ContentStore.UtilitiesCore", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client.Cache", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.Libraries", "Microsoft.BuildXL.Cache.ContentStore.Hashing", "Microsoft.BuildXL.Cache.ContentStore.Interfaces", "Microsoft.BuildXL.Cache.ContentStore.UtilitiesCore", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.Windows.Debuggers.SymstoreInterop", version: "1.0.1-netstandard2.0" },
    { id: "Symbol.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.ContentStore.Hashing", "Microsoft.BuildXL.Cache.ContentStore.UtilitiesCore", "DotNetFxRefAssemblies.Corext"] },
    { id: "TransientFaultHandling.Core", version: "5.1.1209.1" },
    { id: "Microsoft.VisualStudio.Services.Symbol.App.Core", version: "0.2.391" },
    { id: "Microsoft.VisualStudio.Services.Symbol.App.Indexer", version: "0.2.391" },
    { id: "Microsoft.SymbolStore", version: "1.0.560502" },
    { id: "Microsoft.FileFormats", version: "1.0.560502" },

    // Cpp Sdk
    { id: "VisualCppTools.Internal.VS2017Layout", version: "14.39.33521", osSkip: [ "macOS", "unix" ] },

    // SBOM Generation
    // The following two packages need to skip some of its dependencies because they are referencing
    // their net6.0 version and our nuget resolver doesn't handle the case where the framework is gone in newer versions.
    // TODO: we should be able to remove the skipped packages whenever Microsoft.SbomCore
    // expose updated versions that point to the latest (net8 only) SBOM packages
    { id: "Microsoft.SbomCore", version: "6.1.4", dependentPackageIdsToSkip: ["Microsoft.Sbom.Parsers.Spdx22SbomParser"] },
    { id: "Microsoft.Sbom.Common", version: "4.1.2" },
    { id: "Microsoft.Sbom.Parsers.Spdx22SbomParser", version: "4.1.2" },
    { id: "Microsoft.Sbom.Adapters", version: "4.1.2" },
    { id: "Microsoft.ComponentDetection.Contracts", version: "6.0.0", dependentPackageIdsToSkip: ["System.Threading.Tasks.Dataflow"] },
    { id: "Microsoft.Sbom.Contracts", version: "4.1.2" },
    { id: "Microsoft.Sbom.Extensions", version: "4.1.2" },

    // Process remoting
    { id: "AnyBuild.SDK", version: "0.2.0" },

    // Part of VSSDK used by IDE/VsIntegration
    { id: "Microsoft.Internal.VisualStudio.Interop", version: "17.2.32405.191" },
    { id: "Microsoft.VisualStudio.ProjectSystem", version: "17.3.74-pre" },

    // RoslynAnalyzers internal analyzers
    { id: "Microsoft.Internal.Analyzers", version: "2.6.11"},

    // CredScan
    { id: "Microsoft.Security.Utilities.Internal", version: "1.19.0" },

    // Authentication
    { id: "Microsoft.Artifacts.Authentication", version: "0.2.2" },
    
] : [

    // Artifact packages and dependencies in OSS
    { id: "Microsoft.VisualStudio.Services.Client", version: "19.245.0-preview", dependentPackageIdsToSkip: [ "Microsoft.Data.SqlClient", "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "System.Security.Cryptography.OpenSsl", "Microsoft.Data.SqlClient", "System.Security.Principal.Windows" ] },
    { id: "Microsoft.TeamFoundationServer.Client", version: "19.245.0-preview"},
    { id: "Microsoft.TeamFoundation.DistributedTask.Common.Contracts", version: "19.245.0-preview"},
];

// This contains facade modules for the packages that are only available internally
export const resolver = {
    kind: "SourceResolver",
    modules: [
        f`Private/InternalSdk/BuildXL.DeviceMap/module.config.dsc`,
        f`Private/InternalSdk/CB.QTest/module.config.dsc`,
        f`Private/InternalSdk/Drop/module.config.dsc`,
        f`Private/InternalSdk/BuildXL.Tracing.AriaTenantToken/module.config.dsc`,
        f`Private/InternalSdk/AnyBuild.SDK/module.config.dsc`,
        f`Private/InternalSdk/Microsoft.Internal.VisualStudio.Interop/module.config.dsc`,
        f`Private/InternalSdk/Microsoft.VisualStudio.ProjectSystem/module.config.dsc`,
        f`Private/InternalSdk/DeduplicationSigned/module.config.dsc`,
    ]
};
