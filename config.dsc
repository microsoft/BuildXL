// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

config({
    // No orphan projects are owned by this configuration.
    projects: [],

    // Packages that define the build extent.
    modules: [
        ...globR(d`Public/Src`, "module.config.dsc"),
        ...globR(d`Public/Sdk/UnitTests`, "module.config.dsc"),
        ...globR(d`Private/Wdg`, "module.config.dsc"),
        ...globR(d`Private/QTest`, "module.config.dsc"),
        ...globR(d`Private/InternalSdk`, "module.config.dsc"),
        ...globR(d`Private/Tools`, "module.config.dsc"),
        ...globR(d`Public/Sdk/SelfHost`, "module.config.dsc"),

        // Internal only modules
        ...addIf(importFile(f`config.microsoftInternal.dsc`).isMicrosoftInternal,
           ...globR(d`Private/CloudTest`, "module.config.dsc")
        ),
    ],

    frontEnd: {
        enabledPolicyRules: [
            "NoTransformers",
        ]
    },

    resolvers: [
        // These are the new cleaned up Sdk's
        {
            kind: "DScript",
            modules: [
                f`Public/Sdk/Public/Prelude/package.config.dsc`, // Prelude cannot be named module because it is a v1 module
                f`Public/Sdk/Public/Transformers/package.config.dsc`, // Transformers cannot be renamed yet because office relies on the filename
                ...globR(d`Public/Sdk`, "module.config.dsc"),
            ]
        },
        {
            kind: "Nuget",

            // This configuration pins people to a specific version of nuget
            // The credential provider should be set by defining the env variable NUGET_CREDENTIALPROVIDERS_PATH.  TODO: It can be alternatively pinned here,
            // but when it fails to download (e.g. from a share) the build is aborted. Consider making the failure non-blocking.
            configuration: {
                toolUrl: "https://dist.nuget.org/win-x86-commandline/v4.9.4/NuGet.exe",
                hash: "17E8C8C0CDCCA3A6D1EE49836847148C4623ACEA5E6E36E10B691DA7FDC4C39200"
            },

            repositories: importFile(f`config.microsoftInternal.dsc`).isMicrosoftInternal
                ? {
                    "BuildXL.Selfhost": "https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json",
                    // Note: From a compliance point of view it is important that MicrosoftInternal has a single feed.
                    // If you need to consume packages make sure they are upstreamed in that feed.
                  }
                : {
                    "buildxl-selfhost" : "https://pkgs.dev.azure.com/ms/BuildXL/_packaging/BuildXL.Selfhost/nuget/v3/index.json",
                    "nuget.org" : "http://api.nuget.org/v3/index.json",
                    "roslyn-tools" : "https://dotnet.myget.org/F/roslyn-tools/api/v3/index.json",
                    "msbuild" : "https://dotnet.myget.org/F/msbuild/api/v3/index.json",
                    "dotnet-core" : "https://dotnet.myget.org/F/dotnet-core/api/v3/index.json",
                    "dotnet-arcade" : "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json",
                  },

            packages: [

                { id: "NLog", version: "4.6.8" },

                { id: "Bond.Core.CSharp", version: "8.0.0" },
                { id: "Bond.CSharp", version: "8.0.0" },
                { id: "Bond.CSharp.osx-x64", version: "8.0.0" },
                { id: "Bond.Runtime.CSharp", version: "8.0.0" },
                { id: "CLAP", version: "4.6" },
                { id: "CLAP-DotNetCore", version: "4.6" },

                { id: "RuntimeContracts", version: "0.3.0" },

                { id: "Microsoft.NETFramework.ReferenceAssemblies.net451", version: "1.0.0-alpha-5", osSkip: [ "macOS", "unix" ] },
                { id: "Microsoft.NETFramework.ReferenceAssemblies.net461", version: "1.0.0-alpha-5", osSkip: [ "macOS", "unix" ] },
                { id: "Microsoft.NETFramework.ReferenceAssemblies.net462", version: "1.0.0-alpha-5" },
                { id: "Microsoft.NETFramework.ReferenceAssemblies.net472", version: "1.0.0-alpha-5" },

                { id: "EntityFramework", version: "6.0.0" },

                { id: "System.Diagnostics.DiagnosticSource", version: "4.5.0" },
                { id: "System.Diagnostics.DiagnosticSource", version: "4.0.0-beta-23516", alias: "System.Diagnostics.DiagnosticsSource.ForEventHub"},

                // Roslyn
                { id: "Microsoft.Net.Compilers", version: "3.3.1" }, // Update Public/Src/Engine/UnitTests/Engine/Test.BuildXL.Engine.dsc if you change the version of Microsoft.Net.Compilers.
                { id: "Microsoft.NETCore.Compilers", version: "3.3.1" },
                { id: "Microsoft.CodeAnalysis.Common", version: "3.4.0" },
                { id: "Microsoft.CodeAnalysis.CSharp", version: "3.4.0" },
                { id: "Microsoft.CodeAnalysis.VisualBasic", version: "3.4.0" },
                { id: "Microsoft.CodeAnalysis.Workspaces.Common", version: "3.4.0",
                    dependentPackageIdsToSkip: ["SQLitePCLRaw.bundle_green", "System.Composition"],
                    dependentPackageIdsToIgnore: ["SQLitePCLRaw.bundle_green", "System.Composition"],
                },
                { id: "Microsoft.CodeAnalysis.CSharp.Workspaces", version: "3.4.0" },
                { id: "Microsoft.CodeAnalysis.VisualBasic.Workspaces", version: "3.4.0" },

                // Old code analysis libraries, for tests only
                { id: "Microsoft.CodeAnalysis.Common", version: "2.10.0", alias: "Microsoft.CodeAnalysis.Common.Old" },
                { id: "Microsoft.CodeAnalysis.CSharp", version: "2.10.0", alias: "Microsoft.CodeAnalysis.CSharp.Old" },
                { id: "Microsoft.CodeAnalysis.VisualBasic", version: "2.10.0", alias: "Microsoft.CodeAnalysis.VisualBasic.Old" },

                // Roslyn Analyzers
                { id: "Microsoft.CodeAnalysis.Analyzers", version: "2.6.3" },
                { id: "Microsoft.CodeAnalysis.FxCopAnalyzers", version: "2.6.3" },
                { id: "Microsoft.CodeQuality.Analyzers", version: "2.3.0-beta1" },
                { id: "Microsoft.NetFramework.Analyzers", version: "2.3.0-beta1" },
                { id: "Microsoft.NetCore.Analyzers", version: "2.3.0-beta1" },

                { id: "AsyncFixer", version: "1.1.5" },
                { id: "ErrorProne.NET.CoreAnalyzers", version: "0.1.2" },
                { id: "RuntimeContracts.Analyzer", version: "0.3.0" },
                { id: "StyleCop.Analyzers", version: "1.1.0-beta004" },
                { id: "Text.Analyzers", version: "2.3.0-beta1" },

                // MEF
                { id: "Microsoft.Composition", version: "1.0.30" },
                { id: "System.Composition.AttributedModel", version: "1.0.31" },
                { id: "System.Composition.Convention", version: "1.0.31" },
                { id: "System.Composition.Hosting", version: "1.0.31" },
                { id: "System.Composition.Runtime", version: "1.0.31" },
                { id: "System.Composition.TypedParts", version: "1.0.31" },

                { id: "Microsoft.Diagnostics.Tracing.EventSource.Redist", version: "1.1.28" },
                { id: "Microsoft.Diagnostics.Tracing.TraceEvent", version: "2.0.30" },
                { id: "Microsoft.Extensions.Globalization.CultureInfoCache", version: "1.0.0-rc1-final" },
                { id: "Microsoft.Extensions.MemoryPool", version: "1.0.0-rc1-final" },
                { id: "Microsoft.Extensions.PlatformAbstractions", version: "1.1.0" },
                { id: "Microsoft.Extensions.Http", version: "3.1.0" },

                { id: "Microsoft.Tpl.Dataflow", version: "4.5.24" },
                { id: "Microsoft.TypeScript.Compiler", version: "1.8" },
                { id: "Microsoft.WindowsAzure.ConfigurationManager", version: "1.8.0.0" },
                { id: "Newtonsoft.Json", version: "12.0.3" },
                { id: "Newtonsoft.Json.Bson", version: "1.0.1" },
                { id: "System.Data.SQLite.Core", version: "1.0.109.2" },
                { id: "System.Reflection.Metadata", version: "1.6.0" },
                { id: "System.Threading.Tasks.Dataflow", version: "4.9.0" },

                // Nuget
                { id: "NuGet.Commandline", version: "4.7.1" },
                { id: "NuGet.Versioning", version: "4.6.0" }, // Can't use the latest becuase nuget extracts to folder with metadata which we don't support yet.
                { id: "NuGet.Frameworks", version: "5.0.0"}, // needed for qtest on .net core

                // Cpp Sdk
                { id: "VisualCppTools.Community.VS2017Layout", version: "14.11.25506", osSkip: [ "macOS", "unix" ] },

                // ProjFS (virtual file system)
                { id: "Microsoft.Windows.ProjFS", version: "1.2.19351.1" },

                // RocksDb
                { id: "RocksDbSharp", version: "6.6.3-b20200519.4", alias: "RocksDbSharpSigned" },
                { id: "RocksDbNative", version: "6.6.3-b20200519.4" },
                
                { id: "JsonDiffPatch.Net", version: "2.1.0" },

                // Event hubs
                { id: "Microsoft.Azure.Amqp", version: "2.3.5" },
                { id: "Microsoft.Azure.EventHubs", version: "2.1.0",
                    dependentPackageIdsToSkip: ["System.Net.Http", "System.Reflection.TypeExtensions", "System.Runtime.Serialization.Primitives", "Newtonsoft.Json", "System.Diagnostics.DiagnosticSource"],
                },
                { id: "Microsoft.Azure.KeyVault.Core", version: "1.0.0" },
                { id: "Microsoft.Azure.Services.AppAuthentication", version: "1.0.3" },
                { id: "Microsoft.IdentityModel.Logging", version: "5.2.2" },
                { id: "Microsoft.IdentityModel.Tokens", version: "5.2.2",
                    dependentPackageIdsToSkip: ["Newtonsoft.Json"] },
                { id: "System.IdentityModel.Tokens.Jwt", version: "5.2.2",
                    dependentPackageIdsToSkip: ["Newtonsoft.Json"] },

                // Package sets
                ...importFile(f`config.nuget.vssdk.dsc`).pkgs,
                ...importFile(f`config.nuget.aspNetCore.dsc`).pkgs,
                ...importFile(f`config.nuget.dotnetcore.dsc`).pkgs,
                ...importFile(f`config.nuget.grpc.dsc`).pkgs,
                ...importFile(f`config.microsoftInternal.dsc`).pkgs,

                { id: "WindowsAzure.Storage", version: "9.3.3", alias: "WindowsAzure.Storage" },
                { id: "Microsoft.Data.OData", version: "5.8.4" },
                { id: "Microsoft.Data.Services.Client", version: "5.8.2" },
                { id: "System.Spatial", version: "5.8.2" },
                { id: "Microsoft.Data.Edm", version: "5.8.2" },

                // xUnit
                { id: "xunit.abstractions", version: "2.0.3" },
                { id: "xunit.analyzers", version: "0.10.0" },
                { id: "xunit.assert", version: "2.4.1-ms" },
                { id: "xunit.core", version: "2.4.1-ms" },
                { id: "xunit.extensibility.core", version: "2.4.1" },
                { id: "xunit.extensibility.execution", version: "2.4.1" },
                { id: "xunit.runner.console", version: "2.4.1" },
                { id: "microsoft.dotnet.xunitconsolerunner", version: "2.5.1-beta.19270.4" },
                { id: "xunit.runner.reporters", version: "2.4.1-pre.build.4059" },
                { id: "xunit.runner.utility", version: "2.4.1" },
                { id: "xunit.runner.visualstudio", version: "2.4.1", dependentPackageIdsToSkip: ["Microsoft.NET.Test.Sdk"]  },

                // microsoft test platform
                { id: "Microsoft.TestPlatform.TestHost", version: "16.4.0"},
                { id: "Microsoft.TestPlatform.ObjectModel", version: "16.4.0"},
                { id: "Microsoft.NET.Test.Sdk", version: "15.9.0" },
                { id: "Microsoft.CodeCoverage", version: "15.9.0" },

                { id: "Microsoft.IdentityModel.Clients.ActiveDirectory", version: "5.2.6",
                    dependentPackageIdsToSkip: ["Xamarin.Android.Support.CustomTabs", "Xamarin.Android.Support.v7.AppCompat"] },
                { id: "System.Private.Uri", version: "4.3.2" },

                // CloudStore dependencies
                { id: "DeduplicationSigned", version: "1.0.14" },
                { id: "Microsoft.Bcl", version: "1.1.10" },
                { id: "Microsoft.Bcl.Async", version: "1.0.168" },
                { id: "Microsoft.Bcl.AsyncInterfaces", version: "1.1.0" },
                { id: "Microsoft.Bcl.Build", version: "1.0.14" },
                { id: "StackExchange.Redis", version: "2.1.30",
                    dependentPackageIdsToSkip: ["System.IO.Pipelines", "System.Threading.Channels", "Microsoft.Bcl.AsyncInterfaces"] },
                { id: "Pipelines.Sockets.Unofficial", version: "2.1.6",
                    dependentPackageIdsToSkip: ["System.IO.Pipelines", "System.Runtime.CompilerServices.Unsafe", "Microsoft.Bcl.AsyncInterfaces"] },
                { id: "System.Diagnostics.PerformanceCounter", version: "4.7.0" },
                { id: "System.Threading.Channels", version: "4.7.0",
                    dependentPackageIdsToSkip: ["System.Threading.Tasks.Extensions"] },
                { id: "System.Linq.Async", version: "4.0.0"},
                { id: "TransientFaultHandling.Core", version: "5.1.1209.1" },
                { id: "Redis-64", version: "3.0.503", osSkip: [ "macOS", "unix" ] },
                { id: "Redis-osx-x64", version: "1.0.0", osSkip: importFile(f`config.microsoftInternal.dsc`).isMicrosoftInternal
                    ? [ "win" ]
                    : [ "win", "macOS", "unix" ] },

                // Testing
                { id: "System.Security.Cryptography.ProtectedData", version: "4.4.0"},
                { id: "System.Configuration.ConfigurationManager", version: "4.4.0"},
                { id: "FluentAssertions", version: "5.3.0",
                    dependentPackageIdsToSkip: ["System.Reflection.Emit", "System.Reflection.Emit.Lightweight"] },

                { id: "DotNet.Glob", version: "2.0.3" },
                { id: "Minimatch", version: "1.1.0.0" },
                { id: "Microsoft.ApplicationInsights", version: "2.11.0", dependentPackageIdsToIgnore: ["System.RunTime.InteropServices"] },
                { id: "Microsoft.ApplicationInsights.Agent.Intercept", version: "2.0.7" },
                { id: "Microsoft.ApplicationInsights.DependencyCollector", version: "2.3.0" },
                { id: "Microsoft.ApplicationInsights.PerfCounterCollector", version: "2.3.0" },
                { id: "Microsoft.ApplicationInsights.WindowsServer", version: "2.3.0" },
                { id: "Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel", version: "2.3.0" },
                { id: "System.Memory", version: "4.5.1" },
                { id: "System.Runtime.CompilerServices.Unsafe", version: "4.7.0" },
                { id: "System.IO.Pipelines", version: "4.7.0",
                    dependentPackageIdsToSkip: ["System.Threading.Tasks.Extensions"] },
                { id: "System.Security.Cryptography.Xml", version: "4.5.0" },
                { id: "System.Text.Encodings.Web", version: "4.5.0" },
                { id: "System.Security.Permissions", version: "4.5.0" },
                { id: "System.Security.Cryptography.Pkcs", version: "4.5.0" },

                { id: "ILRepack", version: "2.0.16" },

                // VS language service
                { id: "Desktop.Analyzers", version: "1.1.0" },
                { id: "Microsoft.AnalyzerPowerPack", version: "1.0.1" },
                { id: "System.Runtime.Analyzers", version: "1.0.1" },
                { id: "System.Runtime.InteropServices.Analyzers", version: "1.0.1" },
                { id: "System.Security.Cryptography.Hashing.Algorithms.Analyzers", version: "1.1.0" },
                { id: "Nerdbank.FullDuplexStream", version: "1.0.9"},
                { id: "Validation", version: "2.3.7"},

                // VSTS managed API
                { id: "Microsoft.TeamFoundationServer.Client", version: "15.122.1-preview"},
                { id: "Microsoft.TeamFoundation.DistributedTask.WebApi", version: "15.122.1-preview",
                    dependentPackageIdsToSkip: ["*"] },
                { id: "Microsoft.TeamFoundation.DistributedTask.Common", version: "15.112.1"},
                { id: "Microsoft.TeamFoundation.DistributedTask.Common.Contracts", version: "16.137.0-preview"},

                // MSBuild. These should be used for compile references only, as at runtime one can only practically use MSBuilds from Visual Studio / dotnet CLI
                { id: "Microsoft.Build", version: "16.4.0-preview-19516-02",
                    dependentPackageIdsToSkip: ["System.Threading.Tasks.Dataflow", "System.Memory"], // These are overwritten in the deployment by DataflowForMSBuild and SystemMemoryForMSBuild since it doesn't work with the versions we use in larger buildxl.
                },
                { id: "Microsoft.Build.Runtime", version: "16.4.0-preview-19516-02",
                    dependentPackageIdsToSkip: ["System.Threading.Tasks.Dataflow"],
                },
                { id: "Microsoft.Build.Tasks.Core", version: "16.4.0-preview-19516-02",
                    dependentPackageIdsToSkip: ["System.Threading.Tasks.Dataflow"],
                },
                { id: "Microsoft.Build.Utilities.Core", version: "16.4.0-preview-19516-02"},
                { id: "Microsoft.Build.Framework", version: "16.4.0-preview-19516-02"},
                { id: "System.Resources.Extensions", version: "4.6.0-preview9.19411.4",
                    dependentPackageIdsToSkip: ["System.Memory"]},

                // Extra dependencies to make MSBuild work
                { id: "Microsoft.VisualStudio.Setup.Configuration.Interop", version: "1.16.30"},
                { id: "System.CodeDom", version: "4.4.0"},
                { id: "System.Text.Encoding.CodePages", version: "4.5.1",
                    dependentPackageIdsToSkip: ["System.Runtime.CompilerServices.Unsafe"]},
                { id: "System.Memory", version: "4.5.3", alias: "SystemMemoryForMSBuild", dependentPackageIdsToSkip: ["*"]},
                { id: "System.Runtime.CompilerServices.Unsafe", version: "4.5.3", alias: "SystemRuntimeCompilerServicesUnsafeForMSBuild", dependentPackageIdsToSkip: ["*"]},
                
                { id: "System.Numerics.Vectors", version: "4.4.0", alias: "SystemNumericsVectorsForMSBuild"},

                // Used for MSBuild input/output prediction
                { id: "Microsoft.Build.Prediction", version: "0.3.0" },

                { id: "SharpZipLib", version: "1.1.0" },

                // Ninja JSON graph generation helper
                { id: "BuildXL.Tools.Ninjson", version: "0.0.6" },
                { id: "BuildXL.Tools.AppHostPatcher", version: "1.0.0" },

                // CoreRT
                { id: "runtime.osx-x64.Microsoft.DotNet.ILCompiler", version: "1.0.0-alpha-27527-01", osSkip: [ "win", "unix" ] },
                { id: "runtime.win-x64.Microsoft.DotNet.ILCompiler", version: "1.0.0-alpha-27527-01", osSkip: [ "macOS", "unix" ] },

                // Kusto SDK (for netstandard)
                { id: "Microsoft.Azure.Kusto.Cloud.Platform.Azure.NETStandard", version: "6.1.8",
                    dependentPackageIdsToSkip: ["Microsoft.Extensions.PlatformAbstractions"] },
                { id: "Microsoft.Azure.Kusto.Cloud.Platform.NETStandard", version: "6.1.8",
                    dependentPackageIdsToSkip: ["Microsoft.Extensions.PlatformAbstractions"] },
                { id: "Microsoft.Azure.Kusto.Data.NETStandard", version: "6.1.8",
                    dependentPackageIdsToSkip: ["Microsoft.Extensions.PlatformAbstractions"] },
                { id: "Microsoft.Azure.Kusto.Ingest.NETStandard", version: "6.1.8",
                    dependentPackageIdsToSkip: ["Microsoft.Extensions.PlatformAbstractions"] },
                { id: "Microsoft.IO.RecyclableMemoryStream", version: "1.2.2" },
                { id: "Microsoft.Azure.KeyVault", version: "3.0.1"},
                { id: "Microsoft.Azure.KeyVault.WebKey", version: "3.0.1"},
                { id: "Microsoft.Rest.ClientRuntime", version: "3.0.0",
                    dependentPackageIdsToSkip: ["Microsoft.NETCore.Runtime"],
                    dependentPackageIdsToIgnore: ["Microsoft.NETCore.Runtime"],
                },
                { id: "Microsoft.Rest.ClientRuntime.Azure", version: "3.3.18" },
                { id: "Microsoft.NETCore.Windows.ApiSets", version: "1.0.1" },

                // Kusto SDK (for full framework)
                { id: "Microsoft.Azure.Kusto.Data", version: "6.1.8" },
                { id: "Microsoft.Azure.Kusto.Ingest", version: "6.1.8" },
                { id: "Microsoft.Azure.Kusto.Tools", version: "2.2.2" },
                { id: "Microsoft.Azure.Management.Kusto", version: "1.0.0" },

                // ANTLR
                { id: "Antlr4.Runtime.Standard", version: "4.7.2" },

                // Runtime dependencies for Linux
                { 
                    id: "runtime.linux-x64.BuildXL", 
                    version: "0.0.12",
                    osSkip: importFile(f`config.microsoftInternal.dsc`).isMicrosoftInternal 
                        ? [] 
                        : [ "win", "macOS", "unix" ]
                }
            ],

            doNotEnforceDependencyVersions: true,
        },

        importFile(f`config.microsoftInternal.dsc`).resolver,

        {
            kind: "SourceResolver",
            modules: [f`Public\Sdk\SelfHost\Libraries\Dotnet-Runtime-External\module.config.dsc`],
        },
        {
            kind: "Download",
            downloads: [
                // PowerShell.Core
                {
                    moduleName: "PowerShell.Core.win-x64",
                    url: "https://github.com/PowerShell/PowerShell/releases/download/v6.1.3/PowerShell-6.1.3-win-x64.zip",
                    hash: "VSO0:E8E98155383EDFE3CA6D06854638560EAB57C8225880B5308547A916DBE9A9A900",
                    archiveType: "zip",
                },
                {
                    moduleName: "PowerShell.Core.osx-x64",
                    url: "https://github.com/PowerShell/PowerShell/releases/download/v6.1.3/PowerShell-6.1.3-osx-x64.tar.gz",
                    hash: "VSO0:6D3B557962CC26CC9BB6F8A35B288CE8C68460E68B74B73C85BECAE87BB311D600",
                    archiveType: "tgz",
                },
                {
                    moduleName: "PowerShell.Core.linux-x64",
                    url: "https://github.com/PowerShell/PowerShell/releases/download/v6.1.3/PowerShell-6.1.3-linux-x64.tar.gz",
                    hash: "VSO0:159D6D8F82D59AD34D2F8A7084D05C25D6B532DE22CD9502882385F62CDD070300",
                    archiveType: "tgz",
                },

                // XNU kernel sources
                {
                    moduleName: "Apple.Darwin.Xnu",
                    url: "https://github.com/apple/darwin-xnu/archive/xnu-4903.221.2.tar.gz",
                    hash: "VSO0:D6D26AEECA99240D2D833B6B8B811609B9A6E3516C0EE97A951B64F9AA4F90F400",
                    archiveType: "tgz",
                },

                // DotNet Core Runtime
                {
                    moduleName: "DotNet-Runtime.win-x64.3.1.0",
                    url: "https://download.visualstudio.microsoft.com/download/pr/5e1c20ea-113f-47fd-9702-22a8bf1e3974/16bf234b587064709d8e7b58439022d4/dotnet-runtime-3.1.0-win-x64.zip",
                    hash: "VSO0:EE359BDFFFED53EF3C5E76C1716AADD1567447B12A37292C075D6A26F1138C0700",
                    archiveType: "zip",
                },
                {
                    moduleName: "DotNet-Runtime.osx-x64.3.1.0",
                    url: "https://download.visualstudio.microsoft.com/download/pr/454ca582-64f7-4817-bbb0-34a7fb831499/1d2d5613a2d2ebb26da04471e97cb539/dotnet-runtime-3.1.0-osx-x64.tar.gz",
                    hash: "VSO0:FCB44A9D07D3923DB197C05A710FEBBB060649555418A067E04EAE1A06CBCE4400",
                    archiveType: "tgz",
                },
                {
                    moduleName: "DotNet-Runtime.linux-x64.3.1.100",
                    url: "https://download.visualstudio.microsoft.com/download/pr/d731f991-8e68-4c7c-8ea0-fad5605b077a/49497b5420eecbd905158d86d738af64/dotnet-sdk-3.1.100-linux-x64.tar.gz",
                    hash: "VSO0:B89DFFD762BEA6D94E11CEA1C430FDC620CE5407827360085B21963E5887E38300",
                    archiveType: "tgz",
                },
                // The following are needed for dotnet core MSBuild test deployments
                {
                    moduleName: "DotNet-Runtime.win-x64.2.2.2",
                    url: "https://download.visualstudio.microsoft.com/download/pr/b10d0a68-b720-48ae-bab8-4ac39bd1b5d3/f32b8b41dff5c1488c2b915a007fc4a6/dotnet-runtime-2.2.2-win-x64.zip",
                    hash: "VSO0:6BBAE77F9BA0231C90ABD9EA720FF886E8613CE8EF29D8B657AF201E2982829600",
                    archiveType: "zip",
                },
                // NodeJs
                {
                    moduleName: "NodeJs.win-x64",
                    url: "https://nodejs.org/dist/v13.3.0/node-v13.3.0-win-x64.zip",
                    hash: "VSO0:B390393D971687DC5486F5F443ABA914807B9F7DFFD5FD1512F7B6234F2BE2FC00",
                    archiveType: "zip",
                },
                {
                    moduleName: "NodeJs.osx-x64",
                    url: "https://nodejs.org/dist/v13.3.0/node-v13.3.0-darwin-x64.tar.gz",
                    hash: "VSO0:71B123A9120E24D3AB783D277A3649AFB56C97DDB7E79C9568625D51FF29D8CD00",
                    archiveType: "tgz",
                },
                {
                    moduleName: "NodeJs.linux-x64",
                    url: "https://nodejs.org/dist/v13.3.0/node-v13.3.0-linux-x64.tar.gz",
                    hash: "VSO0:4B63D2FDE488809E395B5E6CC0490C65A0AE7BB05C02FC9A1CD641B1C81539AC00",
                    archiveType: "tgz",
                },
                // Rush tests need an LTS (older) version of NodeJs
                {
                    moduleName: "NodeJs.ForRush.win-x64",
                    url: "https://nodejs.org/dist/v12.16.1/node-v12.16.1-win-x64.zip",
                    hash: "VSO0:B65327703FB1775A7ABD637D44816CDE13DFE01BD98FF2B1B1DE8DAC46D1567800",
                    archiveType: "zip",
                },
                {
                    moduleName: "NodeJs.ForRush.osx-x64",
                    url: "https://nodejs.org/dist/v12.16.1/node-v12.16.1-darwin-x64.tar.gz",
                    hash: "VSO0:A3DEEC9D7C133120F255195146072452C6D06D24E7F97754F342627C53A5008000",
                    archiveType: "tgz",
                },
                // Electron
                {
                    moduleName: "Electron.win-x64",
                    url: "https://github.com/electron/electron/releases/download/v2.0.10/electron-v2.0.10-win32-x64.zip",
                    hash: "VSO0:F836344F3D3FEBCD50976B5F33FC2DA64D0753C242C68F61B5908F59CD49B0AB00",
                    archiveType: "zip",
                }
            ],
        },
    ],

    qualifiers: {
        defaultQualifier: {
            configuration: "debug",
            targetFramework: "netcoreapp3.1",
            targetRuntime: 
                Context.getCurrentHost().os === "win" ? "win-x64" :
                Context.getCurrentHost().os === "macOS" ? "osx-x64" : "linux-x64",
        },
        namedQualifiers: {
            Debug: {
                configuration: "debug",
                targetFramework: "netcoreapp3.1",
                targetRuntime: "win-x64",
            },
            DebugNet472: {
                configuration: "debug",
                targetFramework: "net472",
                targetRuntime: "win-x64",
            },
            DebugDotNetCore: {
                configuration: "debug",
                targetFramework: "netcoreapp3.1",
                targetRuntime: "win-x64",
            },
            DebugDotNetCoreMac: {
                configuration: "debug",
                targetFramework: "netcoreapp3.1",
                targetRuntime: "osx-x64",
            },
            DebugLinux: {
                configuration: "debug",
                targetFramework: "netcoreapp3.1",
                targetRuntime: "linux-x64",
            },
            // Release
            Release: {
                configuration: "release",
                targetFramework: "netcoreapp3.1",
                targetRuntime: "win-x64",
            },
            ReleaseNet472: {
                configuration: "release",
                targetFramework: "net472",
                targetRuntime: "win-x64",
            },

            ReleaseDotNetCore: {
                configuration: "release",
                targetFramework: "netcoreapp3.1",
                targetRuntime: "win-x64",
            },
            ReleaseDotNetCoreMac: {
                configuration: "release",
                targetFramework: "netcoreapp3.1",
                targetRuntime: "osx-x64",
            },
            ReleaseLinux: {
                configuration: "release",
                targetFramework: "netcoreapp3.1",
                targetRuntime: "linux-x64",
            },
        }
    },

    mounts: [
        ...importFile(f`macos.mounts.dsc`).mounts,
        {
            name: a`DeploymentRoot`,
            path: p`Out/Bin`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true,
        },
        {
            name: a`CgNpmRoot`,
            path: p`cg/npm`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true
        },
        {
            // Special scrubbable mount with the content that can be cleaned up by running bxl.exe /scrub
            name: a`ScrubbableDeployment`,
            path: Context.getCurrentHost().os === "win" ? p`Out/Objects/TempDeployment` : p`Out/Objects.noindex/TempDeployment`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true,
            isScrubbable: true,
        },
        {
            name: a`SdkRoot`,
            path: p`Public/Sdk/Public`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true,
        },
        {
            name: a`Example`,
            path: p`Example`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true
        },
        {
            name: a`Sandbox`,
            path: p`Public/Src/Sandbox`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true
        },
        {
            name: a`ThirdParty_mono`,
            path: p`third_party/mono@abad3612068e7333956106e7be02d9ce9e346f92`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true
        }
    ],

    searchPathEnumerationTools: [
        r`cl.exe`,
        r`lib.exe`,
        r`link.exe`,
        r`sn.exe`,
        r`csc.exe`,
        r`StyleCopCmd.exe`,
        r`BuildXL.LogGen.exe`,
        r`csc.exe`,
        r`ccrefgen.exe`,
        r`ccrewrite.exe`,
        r`FxCopCmd.exe`,
        r`NuGet.exe`
    ],

    ide: {
        // Let the /VS flag generate the projects in the source tree so that add/remove C# file works properly.
        canWriteToSrc: true,
        dotSettingsFile: f`Public/Sdk/SelfHost/BuildXL/BuildXL.sln.DotSettings`,
    },

    cacheableFileAccessWhitelist: Context.getCurrentHost().os !== "win" ? [] : [
        // Allow the debugger to be able to be launched from BuildXL Builds
        {
            name: "JitDebugger",
            toolPath: f`${Environment.getDirectoryValue("SystemRoot")}/system32/vsjitdebugger.exe`,
            pathRegex: `.*${Environment.getStringValue("CommonProgramFiles").replace("\\", "\\\\")}\\\\Microsoft Shared\\\\VS7Debug\\\\.*`
        },
    ]
});
