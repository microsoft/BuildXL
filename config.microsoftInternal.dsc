// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const isMicrosoftInternal = Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

const artifactNugetVersion = "18.176.30522-buildid13159358";
const azureDevopsNugetVersion = "16.176.0-internal202009221";

// These packages are Microsoft internal packages.
// These consist of internally repackaged products that we can't push to a public feed and have to rely on users installing locally.
// Or they contain code which is internal and can't be open sourced due to tying into Microsoft internal systems.
// The dependent code is still open sourced, but not compiled in the public repo.
export const pkgs = isMicrosoftInternal ? [
    { id: "BuildXL.DeviceMap", version: "0.0.1" },

    // Runtime dependencies used for macOS deployments
    { id: "runtime.osx-x64.BuildXL", version: "3.5.99" },
    { id: "Aria.Cpp.SDK.osx-x64", version: "8.5.6" },
    { id: "Aria.Cpp.SDK.win-x64", version: "8.5.6", osSkip: [ "macOS", "unix" ] },

    { id: "CB.QTest", version: "20.11.9.215614", osSkip: [ "macOS", "unix" ] },

    { id: "BuildXL.Tracing.AriaTenantToken", version: "1.0.0" },

    // Windows Sdk so microsoft dev's don't have to install it.
    { id: "WindowsSdk.Corext", version: "10.0.16299.1", alias: "Windows.Sdk", osSkip: [ "macOS", "unix" ] },

    // Artifact packages and dependencies
    { id: "Microsoft.VisualStudio.Services.ArtifactServices.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "Microsoft.Azure.Cosmos.Table", "Microsoft.Azure.Storage.Blob"] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces"] },
    { id: "Microsoft.VisualStudio.Services.Client", version: azureDevopsNugetVersion, dependentPackageIdsToSkip: [ "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "System.Security.Cryptography.OpenSsl", "System.Security.Principal.Windows" ] },
    { id: "Microsoft.VisualStudio.Services.InteractiveClient", version: azureDevopsNugetVersion, dependentPackageIdsToSkip: [ "Ben.Demystifier" ], dependentPackageIdsToIgnore: [ "Ben.Demystifier" ] },
    { id: "Microsoft.Azure.Storage.Common", version:"11.1.0" },

    { id: "GVFS.Installers", version: "0.3.20147.1" },

    // DropDaemon Artifact dependencies.
    // Here, even though the packages depend on Cache bits other than Hashing, we make sure that the codepaths that actually depend on them are never activated. This is to ensure that there is no cyclic dependency between BXL and AzureDevOps.
    // This is further enforced by not including Cache bits in DropDaemon, other than BuildXL.Cache.Hashing.
    { id: "ArtifactServices.App.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing"] },
    { id: "ArtifactServices.App.Shared.Cache", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.Utilities"] },
    { id: "Drop.App.Core", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.Utilities"] },
    { id: "Drop.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing"] },
    { id: "ItemStore.Shared", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing"] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client.Cache", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.Utilities"] },
    { id: "Microsoft.Windows.Debuggers.SymstoreInterop", version: "1.0.1" },
    { id: "Symbol.App.Core", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.Utilities"] },
    { id: "Symbol.Client", version: artifactNugetVersion, dependentPackageIdsToSkip: ["*"] },

    // CloudTest internal dependencies
    { id: "GvfsTestHelpersForBuildXL", version: "0.1.0"},

    // Internal pacakged version to avoid downloading from the web but the trusted stable internal feed:
    { id: "PowerShell.Core", version: "6.1.0", osSkip: [ "macOS", "unix" ] },

    // Officially mantained CB package that contains Yarn. Used for Yarn tests.
    { id: "Npm.OnCloudBuild", version: "3.1.0" },

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
            f`Private/InternalSdk/Windows.Sdk/module.config.dsc`
        ),

        f`Private/InternalSdk/Drop/module.config.dsc`,
        f`Private/InternalSdk/BuildXL.Tracing.AriaTenantToken/module.config.dsc`,
    ]
};
