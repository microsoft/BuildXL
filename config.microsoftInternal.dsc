// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const isMicrosoftInternal = Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

const artifactNugetVersion = "19.244.35206-buildid28914915";
const azureDevopsNugetVersion = "19.244.0-internal202408064";

// These packages are Microsoft internal packages.
// These consist of internally repackaged products that we can't push to a public feed and have to rely on users installing locally.
// Or they contain code which is internal and can't be open sourced due to tying into Microsoft internal systems.
// The dependent code is still open sourced, but not compiled in the public repo.
export const pkgs = isMicrosoftInternal ? [
    { id: "BuildXL.DeviceMap", version: "0.0.1" },

    // Metrics library used by .net core CaSaaS
    // Todo: Migrade to OpenTelemetry. See https://eng.ms/docs/products/geneva/collect/instrument/ifx/ifx-retirement
    {id: "Microsoft.Cloud.InstrumentationFramework", version: "3.5.1.1"},
    // Temporary workaround for Bond issue. Microsoft.Cloud.InstrumentationFramework is using 13.0.0.
    // Remove Bond once migration to OpenTelemetry is done.
    { id: "Bond.Core.CSharp", version: "13.0.0" },
    { id: "Bond.CSharp", version: "13.0.0" },
    { id: "Bond.Runtime.CSharp", version: "13.0.0" },

    // Runtime dependencies used for macOS deployments
    { id: "runtime.osx-x64.BuildXL", version: "3.8.99" },
    { id: "Aria.Cpp.SDK.win-x64", version: "8.5.6", osSkip: [ "macOS", "unix" ] },
    // cross-plat Aria SDK and its dependencies
    { id: "Microsoft.Applications.Events.Server", version: "1.1.3.308", dependentPackageIdsToIgnore: [ "Microsoft.Data.SQLite" ] },
    { id: "Microsoft.Data.Sqlite", version: "1.1.1" },
    { id: "SQLite", version: "3.13.0" },

    { id: "CB.QTest", version: "24.6.26.153636", osSkip: [ "macOS", "unix" ] },

    { id: "BuildXL.Tracing.AriaTenantToken", version: "1.0.0" },

    // Artifact packages and dependencies
    { id: "Microsoft.VisualStudio.Services.ArtifactServices.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: [
        "Microsoft.BuildXL.Cache.Hashing",
        "Microsoft.BuildXL.Cache.Interfaces",
        "Microsoft.Azure.Cosmos.Table",
        "Microsoft.Azure.Storage.Blob",
        "DotNetFxRefAssemblies.Corext",
        "Mono.Unix",
        "Microsoft.Identity.Client.Desktop" ] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.VisualStudio.Services.Client", version: azureDevopsNugetVersion, dependentPackageIdsToSkip: [ "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "Microsoft.Data.SqlClient", "System.Security.Cryptography.OpenSsl", "System.Security.Principal.Windows" ] },
    { id: "Microsoft.VisualStudio.Services.InteractiveClient", version: azureDevopsNugetVersion, dependentPackageIdsToSkip: [ "Ben.Demystifier" ], dependentPackageIdsToIgnore: [ "Ben.Demystifier" ] },
    { id: "Microsoft.Azure.Storage.Common", version: "11.2.3" },
    { id: "System.ServiceProcess.ServiceController", version: "6.0.1" },

    // CloudStore dependencies
    { id: "DeduplicationSigned", version: "1.0.14" },

    // DropDaemon Artifact dependencies.
    // Here, even though the packages depend on Cache bits other than Hashing, we make sure that the codepaths that actually depend on them are never activated. This is to ensure that there is no cyclic dependency between BXL and AzureDevOps.
    // This is further enforced by not including Cache bits in DropDaemon, other than BuildXL.Cache.Hashing.
    { id: "ArtifactServices.App.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "DotNetFxRefAssemblies.Corext"] },
    { id: "ArtifactServices.App.Shared.Cache", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.Libraries", "Microsoft.BuildXL.Utilities", "Microsoft.BuildXL.Utilities.Core", "Microsoft.BuildXL.Native", "DotNetFxRefAssemblies.Corext", "System.Data.SQLite.Core"] },
    { id: "Drop.App.Core", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.Libraries", "Microsoft.BuildXL.Utilities", "Microsoft.BuildXL.Utilities.Core", "Microsoft.BuildXL.Native", "DotNetFxRefAssemblies.Corext", "System.Data.SQLite.Core"] },
    { id: "Drop.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "DotNetFxRefAssemblies.Corext"] },
    { id: "ItemStore.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client.Cache", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.Libraries", "DotNetFxRefAssemblies.Corext"] },
    { id: "Microsoft.Windows.Debuggers.SymstoreInterop", version: "1.0.1-netstandard2.0" },
    { id: "Symbol.App.Core", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "Microsoft.BuildXL.Cache.Interfaces", "Microsoft.BuildXL.Cache.Libraries", "DotNetFxRefAssemblies.Corext"] },
    { id: "Symbol.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["Microsoft.BuildXL.Cache.Hashing", "DotNetFxRefAssemblies.Corext"] },
    { id: "TransientFaultHandling.Core", version: "5.1.1209.1" },

    // IcM and dependencies
    { id: "Microsoft.AzureAd.Icm.Types.amd64", version: "2.2.1363.11" },
    { id: "Microsoft.AzureAd.Icm.WebService.Client.amd64", version: "2.2.1363.11" },

    // Cpp Sdk
    { id: "VisualCppTools.Internal.VS2017Layout", version: "14.39.33521", osSkip: [ "macOS", "unix" ] },

    // SBOM Generation
    { id: "Microsoft.SBOMCore", version: "4.0.15" },
    { id: "Microsoft.Parsers.ManifestGenerator", version: "3.8.11", dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing"] },
    { id: "Microsoft.Sbom.Parsers.Spdx22SbomParser", version: "2.2.9" },
    { id: "Microsoft.Sbom.Adapters", version: "2.2.9" },
    { id: "Microsoft.ComponentDetection.Contracts", version: "4.9.6" },
    { id: "Microsoft.Sbom.Contracts", version: "2.2.9" },
    { id: "Microsoft.Sbom.Extensions", version: "2.2.9" },
    { id: "Microsoft.Bcl.HashCode", version: "1.1.1" },

    // Process remoting
    { id: "AnyBuild.SDK", version: "0.2.0" },

    // Part of VSSDK used by IDE/VsIntegration
    { id: "Microsoft.Internal.VisualStudio.Interop", version: "17.2.32405.191" },
    { id: "Microsoft.VisualStudio.ProjectSystem", version: "17.3.74-pre" },

    // RoslynAnalyzers internal analyzers
    { id: "Microsoft.Internal.Analyzers", version: "2.6.11"},

    // CredScan
    { id: "Strings.Interop", version: "1.10.0" },
    { id: "RE2.Managed", version: "1.10.0" },
    { id: "Microsoft.Automata.SRM", version: "2.0.0-alpha3" },
    { id: "Microsoft.Security.RegularExpressions", version: "1.7.1.6" } ,
    { id: "Microsoft.Security.CredScan.KnowledgeBase.SharedDomains", version: "1.7.1.6" },
    { id: "Microsoft.Security.CredScan.KnowledgeBase", version: "1.7.1.6" },
    { id: "Microsoft.Security.CredScan.KnowledgeBase.Client", version: "1.7.1.6" },
    { id: "Microsoft.Security.CredScan.KnowledgeBase.Ruleset", version: "1.7.1.6" },

    // Authentication
    { id: "Microsoft.Artifacts.Authentication", version: "0.1.2" },
    
] : [

    // Artifact packages and dependencies in OSS
    { id: "Microsoft.VisualStudio.Services.Client", version: "16.148.0-preview", dependentPackageIdsToSkip: [ "Microsoft.Data.SqlClient", "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "System.Security.Cryptography.OpenSsl", "Microsoft.Data.SqlClient", "System.Security.Principal.Windows" ] },

];

// This contains facade modules for the packages that are only available internally
export const resolver = {
    kind: "SourceResolver",
    modules: [
        f`Private/InternalSdk/BuildXL.DeviceMap/module.config.dsc`,
        f`Private/InternalSdk/CB.QTest/module.config.dsc`,
        ...addIf(isMicrosoftInternal,
            f`Private/InternalSdk/InstrumentationFramework/module.config.dsc`
        ),

        f`Private/InternalSdk/Drop/module.config.dsc`,
        f`Private/InternalSdk/BuildXL.Tracing.AriaTenantToken/module.config.dsc`,
        f`Private/InternalSdk/AnyBuild.SDK/module.config.dsc`,
        f`Private/InternalSdk/Microsoft.Internal.VisualStudio.Interop/module.config.dsc`,
        f`Private/InternalSdk/Microsoft.VisualStudio.ProjectSystem/module.config.dsc`,
        f`Private/InternalSdk/DeduplicationSigned/module.config.dsc`,
    ]
};
