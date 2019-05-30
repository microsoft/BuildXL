// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const isMicrosoftInternal = Environment.getFlag("[Sdk.BuildXL]microsoftInternal");

// These packages are Microsoft internal packages.
// These consist of internally repackaged products that we can't push to a public feed and have to rely on users installing locally.
// Or they contain code which is internal and can't be open sourced due to tying into Microsoft internal systems.
// The dependent code is still open sourced, but not compiled in the public repo.
export const pkgs = isMicrosoftInternal ? [
    { id: "Bond.NET", version: "3.2.0" },
    { id: "Bond.Core.NET", version: "3.2.0" },
    { id: "Bond.Rpc.NET", version: "3.2.0" },
    { id: "BuildXL.DeviceMap", version: "0.0.1" },
    { id: "Microsoft.Applications.Telemetry.Desktop", version: "1.1.152" },

    // Runtime dependencies used for macOS deployments
    { id: "runtime.osx-x64.BuildXL", version: "1.92.99" },
    { id: "Aria.Cpp.SDK.osx-x64", version: "8.5.4" },

    { id: "CB.QTest", version: "19.5.29.221321" },
    { id: "CloudBuild.VmCommandProxy", version: "19.5.15.220914" },

    { id: "BuildXL.Tracing.AriaTenantToken", version: "1.0.0" },

    // Windows Sdk so microsoft dev's don't have to install it.
    { id: "WindowsSdk.Corext", version: "10.0.16299.1", alias: "Windows.Sdk" },

    // Artifact packages and dependencies
    { id: "Microsoft.VisualStudio.Services.ArtifactServices.Shared", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing"] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces"] },
    { id: "Microsoft.VisualStudio.Services.Client", version: "17.149.20190311.3-release", dependentPackageIdsToSkip: [ "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "System.Security.Cryptography.OpenSsl", "System.Security.Principal.Windows" ] },
    { id: "Microsoft.VisualStudio.Services.InteractiveClient", version: "17.149.20190311.3-release", dependentPackageIdsToSkip: [ "Ben.Demystifier" ], dependentPackageIdsToIgnore: [ "Ben.Demystifier" ] },

    // DropDaemon Artifact dependencies.
    // Here, even though the packages depend on Cache bits other than Hashing, we make sure that the codepaths that actually depend on them are never activated. This is to ensure that there is no cyclic dependency between BXL and AzureDevOps.
    // This is further enforced by not including Cache bits in DropDaemon, other than BuildXL.Cache.Hashing.
    { id: "ArtifactServices.App.Shared", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing"] },
    { id: "ArtifactServices.App.Shared.Cache", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.library.forAzDev"] },
    { id: "Drop.App.Core", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.library.forAzDev"] },
    { id: "Drop.Client", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"] },
    { id: "Drop.RemotableClient", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"] },
    { id: "Drop.RemotableClient.Interfaces", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"] },
    { id: "ItemStore.Shared", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing"] },
    { id: "Microsoft.VisualStudio.Services.BlobStore.Client.Cache", version: "17.149.28711-buildid8812231", dependentPackageIdsToSkip: ["*"], dependentPackageIdsToIgnore: ["BuildXL.Cache.Hashing", "BuildXL.Cache.Interfaces", "BuildXL.Cache.Libraries", "BuildXL.library.forAzDev"] },
    { id: "Microsoft.Windows.Debuggers.SymstoreInterop", version: "0.61" },

    // Internal pacakged version to avoid downloading from the web but the trusted stable internal feed:
    { id: "NodeJs", version: "8.12.0-noTest" },
    { id: "PowerShell.Core", version: "6.1.0" },

    // Combined runtimes
    { id: "Dotnet-Runtime", version: "5.0.0" },

] : [

    // Artifact packages and dependencies in OSS
    { id: "Microsoft.VisualStudio.Services.Client", version: "16.148.0-preview", dependentPackageIdsToSkip: [ "Microsoft.Net.Http", "Microsoft.AspNet.WebApi.Client", "System.Security.Cryptography.OpenSsl", "System.Security.Principal.Windows" ] },

];

// This contains facade modules for the packages that are only availalbe internally
export const resolver = {
    kind: "SourceResolver",
    modules: [
        f`private/InternalSdk/Bond/module.config.dsc`,
        f`private/InternalSdk/Bond.NET/module.config.dsc`,
        f`private/InternalSdk/BuildXL.DeviceMap/module.config.dsc`,
        f`private/InternalSdk/Microsoft.Applications.Telemetry.Desktop/module.config.dsc`,
        f`private/InternalSdk/CB.QTest/module.config.dsc`,
        ...addIf(isMicrosoftInternal,
            f`private/InternalSdk/PowerShell.Core/module.config.dsc`,
            f`private/InternalSdk/NodeJs/module.config.dsc`,
            f`private/InternalSdk/DotNet-Runtime/module.config.dsc`,
            f`private/InternalSdk/Windows.Sdk/module.config.dsc`
        ),

        f`private/InternalSdk/Drop/module.config.dsc`,
        f`private/InternalSdk/BuildXL.Tracing.AriaTenantToken/module.config.dsc`,
    ]
};
