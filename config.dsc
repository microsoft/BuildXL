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
        ...globR(d`Private/AdoBuildRunner`, "module.config.dsc"),
        ...globR(d`Private/InternalSdk`, "module.config.dsc"),
        ...globR(d`Private/Tools`, "module.config.dsc"),
        ...globR(d`Public/Sdk/SelfHost`, "module.config.dsc"),
    ],

    frontEnd: {
        enabledPolicyRules: [
            "NoTransformers",
            "EnforceSomeTypeSanity"
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
            // The credential provider should be set by defining the env variable NUGET_CREDENTIALPROVIDERS_PATH.
            kind: "Nuget",

            esrpSignConfiguration :  Context.getCurrentHost().os === "win" && Environment.getFlag("ENABLE_ESRP") ? {
                signToolPath: p`${Environment.expandEnvironmentVariablesInString(Environment.getStringValue("SIGN_TOOL_PATH"))}`,
                signToolConfiguration: Environment.getPathValue("ESRP_SESSION_CONFIG"),
                signToolEsrpPolicy: Environment.getPathValue("ESRP_POLICY_CONFIG"),
                signToolAadAuth: p`${Context.getMount("SourceRoot").path}/Secrets/CodeSign/EsrpAuthentication.json`,
            } : undefined,

            repositories: importFile(f`config.microsoftInternal.dsc`).isMicrosoftInternal
                ? {
                    // If nuget resolver failed to download VisualCpp tool, then download it
                    // manually from "BuildXL.Selfhost" feed into some folder, and specify
                    // that folder as the value of "MyInternal" feed below.
                    // "MyInternal": "E:/BuildXLInternalRepos/NuGetInternal",
                    // CODESYNC: bxl.sh, Shared\Scripts\bxl.ps1
                    "BuildXL.Selfhost": "https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json",
                    // Note: From a compliance point of view it is important that MicrosoftInternal has a single feed.
                    // If you need to consume packages make sure they are upstreamed in that feed.
                  }
                : {
                    "buildxl-selfhost" : "https://pkgs.dev.azure.com/mseng/PipelineTools/_packaging/BuildXL.External.Dependencies/nuget/v3/index.json",
                    "nuget.org" : "https://api.nuget.org/v3/index.json",
                    "dotnet-arcade" : "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-eng/nuget/v3/index.json",
                  },

            packages: [
                { id: "NLog", version: "4.7.7" },
                { id: "CLAP", version: "4.6" },
                { id: "CLAP-DotNetCore", version: "4.6" },

                { id: "RuntimeContracts", version: "0.5.0" }, // Be very careful with updating this version, because CloudBuild and other repository needs to be updated as will
                { id: "RuntimeContracts.Analyzer", version: "0.4.3" }, // The versions are different because the analyzer has higher version for now.

                { id: "Microsoft.NETFramework.ReferenceAssemblies.net472", version: "1.0.0" },

                { id: "System.Diagnostics.DiagnosticSource", version: "9.0.2" },

                // Roslyn
                // The old compiler used by integration tests only.
                { id: "Microsoft.Net.Compilers", version: "4.0.1" }, // Update Public/Src/Engine/UnitTests/Engine/Test.BuildXL.Engine.dsc if you change the version of Microsoft.Net.Compilers.
                { id: "Microsoft.NETCore.Compilers", version: "4.0.1" },
                // The package with an actual csc.dll
                { id: "Microsoft.Net.Compilers.Toolset", version: "4.8.0" },

                // These packages are used by log generators and because they're old
                // we can't use the latest language features there.
                { id: "Microsoft.CodeAnalysis.Common", version: "3.8.0" },
                { id: "Microsoft.CodeAnalysis.CSharp", version: "3.8.0" },
                { id: "Microsoft.CodeAnalysis.VisualBasic", version: "3.8.0" },
                { id: "Microsoft.CodeAnalysis.Workspaces.Common", version: "3.8.0",
                    dependentPackageIdsToSkip: ["SQLitePCLRaw.bundle_green", "System.Composition"],
                    dependentPackageIdsToIgnore: ["SQLitePCLRaw.bundle_green", "System.Composition"],
                },
                { id: "Microsoft.CodeAnalysis.CSharp.Workspaces", version: "3.8.0" },

                { id: "Humanizer.Core", version: "2.2.0" },

                // Old code analysis libraries, for tests only
                { id: "Microsoft.CodeAnalysis.Common", version: "2.10.0", alias: "Microsoft.CodeAnalysis.Common.Old" },
                { id: "Microsoft.CodeAnalysis.CSharp", version: "2.10.0", alias: "Microsoft.CodeAnalysis.CSharp.Old", dependentPackageIdsToSkip: ["Microsoft.CodeAnalysis.Common"] },
                { id: "Microsoft.CodeAnalysis.VisualBasic", version: "2.10.0", alias: "Microsoft.CodeAnalysis.VisualBasic.Old" },

                // Roslyn Analyzers
                { id: "Microsoft.CodeAnalysis.Analyzers", version: "3.3.1" },
                { id: "Microsoft.CodeAnalysis.FxCopAnalyzers", version: "2.6.3" },
                { id: "Microsoft.CodeQuality.Analyzers", version: "2.6.3" },
                { id: "Microsoft.NetFramework.Analyzers", version: "2.6.3" },
                { id: "Microsoft.NetCore.Analyzers", version: "2.6.3" },
                { id: "Microsoft.CodeAnalysis.NetAnalyzers", version: "5.0.3"},

                { id: "AsyncFixer", version: "1.6.0" },
                { id: "ErrorProne.NET.CoreAnalyzers", version: "0.6.1-beta.1" },
                { id: "protobuf-net.BuildTools", version: "3.0.101" },
                { id: "Microsoft.VisualStudio.Threading.Analyzers", version: "17.6.40"},
                { id: "Text.Analyzers", version: "2.6.3" },
                { id: "Microsoft.CodeAnalysis.PublicApiAnalyzers", version: "3.3.4" },

                // MEF
                { id: "Microsoft.Composition", version: "1.0.30" },
                { id: "System.Composition.AttributedModel", version: "1.0.31" },
                { id: "System.Composition.Convention", version: "1.0.31" },
                { id: "System.Composition.Hosting", version: "1.0.31" },
                { id: "System.Composition.Runtime", version: "1.0.31" },
                { id: "System.Composition.TypedParts", version: "1.0.31" },

                { id: "Microsoft.Diagnostics.Tracing.EventSource.Redist", version: "1.1.28" },
                { id: "Microsoft.Diagnostics.Tracing.TraceEvent", version: "3.0.7" },
                { id: "Microsoft.Extensions.Globalization.CultureInfoCache", version: "1.0.0-rc1-final" },
                { id: "Microsoft.Extensions.MemoryPool", version: "1.0.0-rc1-final" },
                { id: "Microsoft.Extensions.PlatformAbstractions", version: "1.1.0" },
                { id: "Microsoft.Extensions.Http", version: "7.0.0" },

                { id: "Microsoft.Tpl.Dataflow", version: "4.5.24" },
                { id: "Microsoft.TypeScript.Compiler", version: "1.8" },
                { id: "Microsoft.WindowsAzure.ConfigurationManager", version: "1.8.0.0" },
                { id: "Newtonsoft.Json", version: "13.0.3" },
                { id: "Newtonsoft.Json.Bson", version: "1.0.1" },
                { id: "System.Reflection.Metadata", version: "8.0.0" },
                // The VBCS logger is used by QuickBuild and runs in the context of old VS installations, so it cannot use a higher version
                // Please do not upgrade this dll (or if you do, make sure this happens in coordination with the QuickBuild team)
                { id: "System.Reflection.Metadata", version: "5.0.0", alias: "System.Reflection.Metadata.ForVBCS" },

                { id: "System.Threading.Tasks.Dataflow", version: "8.0.0" },

                // Nuget
                { id: "NuGet.Packaging", version: "6.9.1" },
                { id: "NuGet.Configuration", version: "6.9.1" },
                { id: "NuGet.Common", version: "6.9.1" },
                { id: "NuGet.Protocol", version: "6.9.1" },
                { id: "NuGet.Versioning", version: "6.9.1" }, 
                { id: "NuGet.CommandLine", version: "6.9.1" },
                { id: "NuGet.Frameworks", version: "6.9.1"}, // needed for qtest on .net core

                // ProjFS (virtual file system)
                { id: "Microsoft.Windows.ProjFS", version: "1.2.19351.1" },

                // RocksDb
                { id: "RocksDbSharp", version: "8.1.1-20241011.2", alias: "RocksDbSharpSigned" },
                { id: "RocksDbNative", version: "8.1.1-20241011.2" },

                { id: "JsonDiffPatch.Net", version: "2.1.0" },

                // Event hubs
                { id: "Microsoft.Azure.Amqp", version: "2.6.1" },
                { id: "Azure.Core.Amqp", version: "1.3.0"},
                { id: "Azure.Messaging.EventHubs", version: "5.9.0" },
                { id: "Microsoft.Azure.KeyVault.Core", version: "2.0.4" },
                { id: "Microsoft.IdentityModel.Logging", version: "8.1.2" },
                { id: "Microsoft.IdentityModel.Tokens", version: "8.1.2" },
                { id: "System.IdentityModel.Tokens.Jwt", version: "8.1.2" },
                { id: "Microsoft.IdentityModel.JsonWebTokens", version: "8.1.2" },

                // Key Vault
                { id: "Azure.Security.KeyVault.Secrets", version: "4.5.0" },
                { id: "Azure.Security.KeyVault.Certificates", version: "4.5.1" },
                { id: "Azure.Identity", version: "1.11.4" },
                { id: "Microsoft.Identity.Client", version: "4.68.0" },
                { id: "Microsoft.IdentityModel.Abstractions", version: "8.1.2" },
                { id: "Microsoft.Identity.Client.Extensions.Msal", version: "4.68.0" },
                { id: "Azure.Core", version: "1.44.1" },
                { id: "Azure.Identity.Broker", version: "1.1.0" },
                { id: "System.Memory.Data", version: "6.0.0" },
                { id: "System.ClientModel", version: "1.2.1" },

                // Authentication
                { id: "Microsoft.Identity.Client.Broker", version: "4.68.0" },
                { id: "Microsoft.Identity.Client.NativeInterop", version: "0.16.2" },
                { id: "Azure.ResourceManager", version: "1.13.0"},
                
                // Package sets
                ...importFile(f`config.nuget.vssdk.dsc`).pkgs,
                ...importFile(f`config.nuget.aspNetCore.dsc`).pkgs,
                ...importFile(f`config.nuget.dotnetcore.dsc`).pkgs,
                ...importFile(f`config.nuget.grpc.dsc`).pkgs,
                ...importFile(f`config.microsoftInternal.dsc`).pkgs,

                // Azure Blob Storage SDK V12
                { id: "Azure.Storage.Blobs", version: "12.16.0" },
                { id: "Azure.Storage.Common", version: "12.15.0" },
                { id: "System.IO.Hashing", version: "6.0.0" },
                { id: "Azure.Storage.Blobs.Batch", version: "12.10.0" },
                { id: "Azure.Storage.Blobs.ChangeFeed", version: "12.0.0-preview.34" },

                // xUnit
                { id: "xunit.abstractions", version: "2.0.3" },
                { id: "xunit.assert", version: "2.5.3" },
                { id: "xunit.extensibility.core", version: "2.5.3" },
                { id: "xunit.extensibility.execution", version: "2.5.3" },
                { id: "xunit.runner.console", version: "2.5.3" },
                { id: "xunit.runner.visualstudio", version: "2.5.3" },
                { id: "xunit.runner.utility", version: "2.5.3" },
                { id: "xunit.runner.reporters", version: "2.5.3" },
                // comes from https://dev.azure.com/dnceng/public/_artifacts/feed/dotnet-eng/NuGet/Microsoft.DotNet.XUnitConsoleRunner/versions
                { id: "Microsoft.DotNet.XUnitConsoleRunner", version: "2.5.1-beta.22179.1" },

                // SQL
                { id: "Microsoft.Data.SqlClient", version: "5.2.1" },
                { id: "Microsoft.Data.SqlClient.SNI", version: "5.2.0" },
                { id: "Microsoft.Data.SqlClient.SNI.runtime", version: "5.2.0" },
                { id: "Microsoft.IdentityModel.Protocols.OpenIdConnect", version: "6.35.0" },
                { id: "Microsoft.IdentityModel.Protocols", version: "6.35.0" },
                { id: "Microsoft.SqlServer.Server", version: "1.0.0" },
                { id: "System.Runtime.Caching", version: "9.0.3" },

                // Sbom dependencies
                { id: "Serilog", version: "4.3.0" },
                { id: "Serilog.Sinks.Console", version: "6.0.0" },

                // microsoft test platform
                { id: "Microsoft.TestPlatform.TestHost", version: "16.4.0"},
                { id: "Microsoft.TestPlatform.ObjectModel", version: "17.7.2"},
                { id: "Microsoft.NET.Test.Sdk", version: "15.9.0" },
                { id: "Microsoft.CodeCoverage", version: "15.9.0" },

                { id: "System.Private.Uri", version: "4.3.2" },

                // CloudStore dependencies
                { id: "Microsoft.Bcl", version: "1.1.10" },
                { id: "Microsoft.Bcl.Async", version: "1.0.168" },
                { id: "Microsoft.Bcl.AsyncInterfaces", version: "9.0.4" },
                { id: "Microsoft.Bcl.AsyncInterfaces", version: "8.0.0", alias: "Microsoft.Bcl.AsyncInterfaces.v8" },
                { id: "Microsoft.Bcl.Build", version: "1.0.14" },
                { id: "Microsoft.Bcl.HashCode", version: "1.1.1" },
                
                { id: "Pipelines.Sockets.Unofficial", version: "2.2.0" },
                { id: "System.Diagnostics.PerformanceCounter", version: "6.0.0" },
                { id: "System.Threading.Channels", version: "9.0.2" },
                { id: "System.Threading.RateLimiting", version: "7.0.0" },

                { id: "System.Linq.Async", version: "4.0.0"},
                { id: "Polly", version: "7.2.2" },
                { id: "Polly.Contrib.WaitAndRetry", version: "1.1.1" },

                // Azurite node app compiled to standalone executable
                // Sources for this package are: https://github.com/Azure/Azurite
                // This packaged is produced by the pipeline: https://dev.azure.com/mseng/Domino/_build?definitionId=13199
                { id: "BuildXL.Azurite.Executables", version: "1.0.0-CI-20230614-171424" },

                // Testing
                { id: "System.Security.Cryptography.ProtectedData", version: "9.0.3" },
                { id: "System.Configuration.ConfigurationManager", version: "9.0.3" },
                { id: "System.Diagnostics.EventLog", version: "9.0.3" },

                // WARNING: FluentAssertions moved to a commercial license starting from V8.0.0
                // NEVER EVER UPDATE THIS PACKAGE BEYOND V7
                // See: https://github.com/fluentassertions/fluentassertions/pull/2943
                // See: https://xceed.com/fluent-assertions-faq/
                { id: "FluentAssertions", version: "5.3.0" },

                { id: "DotNet.Glob", version: "2.0.3" },
                { id: "Minimatch", version: "2.0.0" },
                { id: "Microsoft.ApplicationInsights", version: "2.22.0", dependentPackageIdsToIgnore: ["System.RunTime.InteropServices"] },
                { id: "Microsoft.ApplicationInsights.Agent.Intercept", version: "2.4.0" },
                { id: "Microsoft.ApplicationInsights.DependencyCollector", version: "2.22.0" },
                { id: "Microsoft.ApplicationInsights.PerfCounterCollector", version: "2.22.0" },
                { id: "Microsoft.ApplicationInsights.WindowsServer", version: "2.22.0" },
                { id: "Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel", version: "2.22.0" },
                { id: "Microsoft.Extensions.Caching.Memory", version: "1.0.0" },
                { id: "Microsoft.Extensions.Caching.Abstractions", version: "1.0.0" },
                { id: "System.Security.Cryptography.Xml", version: "8.0.0" },
                { id: "System.Text.Encodings.Web", version: "9.0.4" },
                { id: "System.Security.Permissions", version: "7.0.0" },
                { id: "System.Windows.Extensions", version: "7.0.0" },
                { id: "System.Drawing.Common", version: "7.0.0" },
                { id: "Microsoft.Win32.SystemEvents", version: "7.0.0" },
                { id: "System.Security.Cryptography.Pkcs", version: "8.0.0" },

                { id: "ILRepack", version: "2.0.16" },

                // VS language service
                { id: "System.Runtime.Analyzers", version: "1.0.1" },
                { id: "System.Runtime.InteropServices.Analyzers", version: "1.0.1" },
                { id: "System.Security.Cryptography.Hashing.Algorithms.Analyzers", version: "1.1.0" },
                { id: "Validation", version: "2.5.42"},

                // VSTS managed API
                { id: "Microsoft.TeamFoundation.DistributedTask.WebApi", version: "19.245.0-preview",
                    dependentPackageIdsToSkip: ["*"] },

                // MSBuild. These should be used for compile references only, as at runtime one can only practically use MSBuilds from Visual Studio / dotnet CLI
                { id: "Microsoft.Build", version: "17.11.48" },
                { id: "Microsoft.Build.Runtime", version: "17.11.48" },
                { id: "Microsoft.Build.Tasks.Core", version: "17.11.48" },
                { id: "Microsoft.Build.Utilities.Core", version: "17.11.48" },
                { id: "Microsoft.Build.Framework", version: "17.11.48" },
                { id: "Microsoft.NET.StringTools", version: "17.11.48" },
                { id: "Microsoft.Build.Locator", version: "1.5.5" },
                { id: "System.Reflection.MetadataLoadContext", version: "8.0.0"},    

                { id: "System.Resources.Extensions", version: "8.0.0",
                    dependentPackageIdsToSkip: ["System.Memory"]},

                // Buffers and Memory
                { id: "System.Buffers", version: "4.5.1" }, /* Change Sync: BuildXLSdk.cacheBindingRedirects() */ // A different version, because StackExchange.Redis uses it.
                { id: "System.Memory", version: "4.5.5" }, /* Change Sync: BuildXLSdk.cacheBindingRedirects() */
                { id: "System.Runtime.CompilerServices.Unsafe", version: "6.0.0" }, /* Change Sync: BuildXLSdk.cacheBindingRedirects() */
                { id: "System.IO.Pipelines", version: "9.0.4" },
                { id: "System.Numerics.Vectors", version: "4.5.0" }, /* Change Sync: BuildXLSdk.cacheBindingRedirects() */

                // Extra dependencies to make MSBuild work
                { id: "Microsoft.VisualStudio.Setup.Configuration.Interop", version: "3.2.2146"},
                { id: "System.CodeDom", version: "8.0.0"},
                { id: "System.Text.Encoding.CodePages", version: "7.0.0" },

                // Used for MSBuild input/output prediction
                { id: "Microsoft.Build.Prediction", version: "1.2.27" },

                { id: "SharpZipLib", version: "1.3.3" },

                { id: "ObjectLayoutInspector", version: "0.1.4" },

                // Ninja JSON graph generation helper
                { id: "BuildXL.Tools.Ninjson", version: "1.11.6", osSkip: [ "macOS" ] },
                { id: "BuildXL.Tools.AppHostPatcher", version: "2.0.0" },

                // Azure Communication
                { id: "Microsoft.Rest.ClientRuntime", version: "2.3.24",
                    dependentPackageIdsToSkip: ["Microsoft.NETCore.Runtime"],
                    dependentPackageIdsToIgnore: ["Microsoft.NETCore.Runtime"],
                },
                { id: "Microsoft.Rest.ClientRuntime.Azure", version: "3.3.19" },

                // ANTLR
                { id: "Antlr4.Runtime.Standard", version: "4.7.2" },

                // For C++ testing
                { id: "boost", version: "1.71.0.0" },

                // Needed for SBOM Generation
                { id: "Microsoft.Extensions.Logging.Abstractions", version: "9.0.0" },
                { id: "packageurl-dotnet", version: "1.1.0" },
                { id: "System.Reactive", version: "6.0.1" },

                // CredScan
                { id: "Crc32.NET", version: "1.2.0" },

                // Windows CoW on ReFS
                { id: "CopyOnWrite", version: "0.3.8" },

                // Windows SDK
                // CODESYNC: This version should be updated together with the version number in Public/Sdk/Experimental/Msvc/WindowsSdk/windowsSdk.dsc
                { id: "Microsoft.Windows.SDK.cpp", version: "10.0.22621.755", osSkip: [ "macOS", "unix" ] },
                { id: "Microsoft.Windows.SDK.CPP.x86", version: "10.0.22621.755", osSkip: [ "macOS", "unix" ] },
                { id: "Microsoft.Windows.SDK.CPP.x64", version: "10.0.22621.755", osSkip: [ "macOS", "unix" ] },

                // AdoBuildRunner brings these along
                { id: "System.CommandLine", version: "2.0.0-beta4.22272.1" },
                { id: "Microsoft.Extensions.Http", version: "8.0.0", alias: "Microsoft.Extensions.Http.v8" },
                { id: "Microsoft.Extensions.Http.Resilience", version: "8.0.0", dependentPackageIdsToSkip: ["Microsoft.Extensions.Http"] },
                { id: "Microsoft.Extensions.Http.Diagnostics", version: "8.0.0", dependentPackageIdsToSkip: ["Microsoft.Extensions.Http"] },
                { id: "Microsoft.Extensions.Resilience", version: "8.0.0" },
                { id: "Microsoft.Extensions.Diagnostics", version: "8.0.0" },
                { id: "Microsoft.Extensions.Logging.Configuration", version: "8.0.0" },
                { id: "Microsoft.Extensions.Diagnostics.ExceptionSummarization", version: "8.0.0" },
                { id: "Microsoft.Extensions.Diagnostics.Abstractions", version: "8.0.0" },
                { id: "Microsoft.Extensions.Compliance.Abstractions", version: "8.0.0" },
                { id: "Microsoft.Extensions.DependencyInjection.AutoActivation", version: "8.0.0"},
                { id: "Microsoft.Extensions.Telemetry", version: "8.0.0" },
                { id: "Microsoft.Extensions.AmbientMetadata.Application", version: "8.0.0" },
                { id: "Microsoft.Extensions.DiagnosticAdapter", version: "3.1.32" },
                { id: "Microsoft.Extensions.Telemetry.Abstractions", version: "8.0.0" },
                { id: "Microsoft.Extensions.Options.ConfigurationExtensions", version: "8.0.0" },
                { id: "Polly.Core", version: "8.0.0" },
                { id: "Polly.Extensions", version: "8.0.0" },
                { id: "Microsoft.Bcl.TimeProvider", version: "8.0.1" },
                { id: "Polly.RateLimiting", version: "8.0.0" },
                { id: "Microsoft.IO.RecyclableMemoryStream", version: "2.3.2" },

                // Compression streams
                { id: "ZstdSharp.Port", version: "0.8.4" },
            ],
        },

        importFile(f`config.microsoftInternal.dsc`).resolver,

        // .NET Runtimes.
        { kind: "SourceResolver", modules: [f`Public\Sdk\SelfHost\Libraries\Dotnet-Runtime-8-External\module.config.dsc`] },
        { kind: "SourceResolver", modules: [f`Public\Sdk\SelfHost\Libraries\Dotnet-Runtime-9-External\module.config.dsc`] },

        {
            kind: "Download",

            downloads: [
                // XNU kernel sources
                {
                    moduleName: "Apple.Darwin.Xnu",
                    url: "https://github.com/apple/darwin-xnu/archive/xnu-4903.221.2.tar.gz",
                    hash: "VSO0:D6D26AEECA99240D2D833B6B8B811609B9A6E3516C0EE97A951B64F9AA4F90F400",
                    archiveType: "tgz",
                },

                // DotNet Core Runtime 9.0.10
                {
                    moduleName: "DotNet-Runtime.win-x64.9.0", 
                    url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.10/dotnet-runtime-9.0.10-win-x64.zip",
                    hash: "VSO0:76705C83EFBADA0D714AD3D52235859654EF34EDD1FA553F6A18BEF448B9979700",
                    archiveType: "zip",
                },
                {
                    moduleName: "DotNet-Runtime.osx-x64.9.0",
                    url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.10/dotnet-runtime-9.0.10-osx-x64.tar.gz",
                    hash: "VSO0:CFDB1C7974B0304DF59DEB1AFC74FD8AAF8333A4B4F5D1925EBD67FA21C7A1F200",
                    archiveType: "tgz",
                },
                {
                    moduleName: "DotNet-Runtime.linux-x64.9.0",
                    url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/9.0.10/dotnet-runtime-9.0.10-linux-x64.tar.gz",
                    hash: "VSO0:5492F7D0BE49201EA23BCF30CD7F011430994DFBD8ECAFBF78F57F9EA20F2B9F00",
                    archiveType: "tgz",
                },

                // DotNet Core Runtime 8.0.21
                {
                    moduleName: "DotNet-Runtime.win-x64.8.0", 
                    url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.21/dotnet-runtime-8.0.21-win-x64.zip",
                    hash: "VSO0:44249E992B84FCEAD45648B44C790BADD48B45CB0E08C4D604F0EE9AD65B72D900",
                    archiveType: "zip",
                },
                {
                    moduleName: "DotNet-Runtime.osx-x64.8.0",
                    url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.21/dotnet-runtime-8.0.21-osx-x64.tar.gz",
                    hash: "VSO0:889B3B2C7BD49EF738616FBDD36BAA4AF8047C5E5DE1F4645DDF6605E86F34E800",
                    archiveType: "tgz",
                },
                {
                    moduleName: "DotNet-Runtime.linux-x64.8.0",
                    url: "https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.21/dotnet-runtime-8.0.21-linux-x64.tar.gz",
                    hash: "VSO0:791BC088C5908E3FAE05A47F3D6B1BE88C3DF5C72A63A8321CEA7E2CE1D4E46D00",
                    archiveType: "tgz",
                },

                // The following are needed for dotnet core MSBuild test deployments
                {
                    moduleName: "DotNet-Runtime.win-x64.2.2.2",
                    url: "https://download.visualstudio.microsoft.com/download/pr/97b97652-4f74-4866-b708-2e9b41064459/7c722daf1a80a89aa8c3dec9103c24fc/dotnet-runtime-2.2.2-linux-x64.tar.gz",
                    hash: "VSO0:6E5172671364C65B06C9940468A62BAF70EE27392CB2CA8B2C8BFE058CCD088300",
                    archiveType: "tgz",
                },
                // NodeJs
                {
                    moduleName: "NodeJs.win-x64",
                    url: "https://nodejs.org/dist/v22.15.0/node-v22.15.0-win-x64.zip",
                    hash: "VSO0:74978BAE2CEA5B33E144919A9EF1634067B213C7E78D822E43DB3048BF3DC8C200",
                    archiveType: "zip",
                },
                {
                    moduleName: "NodeJs.osx-x64",
                    url: "https://nodejs.org/dist/v22.15.0/node-v22.15.0-darwin-x64.tar.gz",
                    hash: "VSO0:9B190DBB92C6F576E178CAFD85C0156719CF65F8D39D7C83E4AB12FC132E269500",
                    archiveType: "tgz",
                },
                {
                    moduleName: "NodeJs.linux-x64",
                    url: "https://nodejs.org/dist/v22.15.0/node-v22.15.0-linux-x64.tar.gz",
                    hash: "VSO0:CBE67C4B307810EB6D62ED80DDD8F9146353BAE2E448B6A4359553384A19BA5000",
                    archiveType: "tgz",
                },
                {
                    moduleName: "YarnTool",
                    extractedValueName: "yarnPackage",
                    url: 'https://registry.npmjs.org/yarn/-/yarn-1.22.19.tgz',
                    archiveType: "tgz"
                },
                {
                    moduleName: "MinGit.win-x64",
                    url: 'https://github.com/git-for-windows/git/releases/download/v2.43.0.windows.1/MinGit-2.43.0-64-bit.zip',
                    hash: 'VSO0:15D4663615814ADAE92F449B78A2668C515BD475DFDAC30384EFF84C8413546700',
                    archiveType: "zip"
                },

                // eBPF sandbox
                // CODESYNC: Public/Src/Sandbox/Linux/ebpf/BuildXL.Sandbox.Linux.eBPF.dsc
                {
                    moduleName: "libbpf",
                    url: "https://github.com/libbpf/libbpf/archive/refs/tags/v1.4.7.tar.gz",
                    hash: "VSO0:5F1C0937CD30C223AA6C0527992637610804D924024AA66E7E4004E0574ED47900",
                    archiveType: "tgz"
                },
                {
                    moduleName: "bpftool",
                    url: "https://github.com/libbpf/bpftool/releases/download/v7.5.0/bpftool-v7.5.0-amd64.tar.gz",
                    hash: "VSO0:DC8B3E7A8B7BC8DC8F7943656AD11FEDA58064FFAB0D8EF3DE8B75FFA0237E0800",
                    archiveType: "tgz"
                }
            ],
        },
    ],

    qualifiers: {
        defaultQualifier: {
            configuration: "debug",
            targetFramework: "net9.0",
            targetRuntime:
                Context.getCurrentHost().os === "win" ? "win-x64" :
                Context.getCurrentHost().os === "macOS" ? "osx-x64" : "linux-x64",
        },
        namedQualifiers: {
            Debug: {
                configuration: "debug",
                targetFramework: "net9.0",
                targetRuntime: "win-x64",
            },
            DebugNet472: {
                configuration: "debug",
                targetFramework: "net472",
                targetRuntime: "win-x64",
            },
            DebugNet9: {
                configuration: "debug",
                targetFramework: "net9.0",
                targetRuntime: "win-x64",
            },
            DebugNet8: {
                configuration: "debug",
                targetFramework: "net8.0",
                targetRuntime: "win-x64",
            },
            DebugDotNetCoreMac: {
                configuration: "debug",
                targetFramework: "net9.0",
                targetRuntime: "osx-x64",
            },
            DebugDotNetCoreMacNet9: {
                configuration: "debug",
                targetFramework: "net9.0",
                targetRuntime: "osx-x64",
            },
            DebugDotNetCoreMacNet8: {
                configuration: "debug",
                targetFramework: "net8.0",
                targetRuntime: "osx-x64",
            },
            DebugLinux: {
                configuration: "debug",
                targetFramework: "net9.0",
                targetRuntime: "linux-x64",
            },
            DebugLinuxNet9: {
                configuration: "debug",
                targetFramework: "net9.0",
                targetRuntime: "linux-x64",
            },
            DebugLinuxNet8: {
                configuration: "debug",
                targetFramework: "net8.0",
                targetRuntime: "linux-x64",
            },
            // Release
            Release: {
                configuration: "release",
                targetFramework: "net9.0",
                targetRuntime: "win-x64",
            },
            ReleaseNet472: {
                configuration: "release",
                targetFramework: "net472",
                targetRuntime: "win-x64",
            },
            ReleaseNet9: {
                configuration: "release",
                targetFramework: "net9.0",
                targetRuntime: "win-x64",
            },
            ReleaseNet8: {
                configuration: "release",
                targetFramework: "net8.0",
                targetRuntime: "win-x64",
            },
            ReleaseDotNetCoreMac: {
                configuration: "release",
                targetFramework: "net9.0",
                targetRuntime: "osx-x64",
            },
            ReleaseDotNetCoreMacNet9: {
                configuration: "release",
                targetFramework: "net9.0",
                targetRuntime: "osx-x64",
            },
            ReleaseDotNetCoreMacNet8: {
                configuration: "release",
                targetFramework: "net8.0",
                targetRuntime: "osx-x64",
            },
            ReleaseLinux: {
                configuration: "release",
                targetFramework: "net9.0",
                targetRuntime: "linux-x64",
            },
            ReleaseLinuxNet9: {
                configuration: "release",
                targetFramework: "net9.0",
                targetRuntime: "linux-x64",
            },
            ReleaseLinuxNet8: {
                configuration: "release",
                targetFramework: "net8.0",
                targetRuntime: "linux-x64",
            },
        }
    },

    mounts: [
        ...importFile(f`unix.mounts.dsc`).mounts,
        {
            name: a`DeploymentRoot`,
            path: p`Out/Bin`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true,
            isScrubbable: true,
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
            path: Context.getCurrentHost().os !== "macOS" ? p`Out/Objects/TempDeployment` : p`Out/Objects.noindex/TempDeployment`,
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
            name: a`NodeJsForUnitTests`,
            path: p`Out/NodeJsForUnitTests`,
            trackSourceFileChanges: true,
            isWritable: true,
            isReadable: true,
            isScrubbable: true,
        },
        {
            name: a`ebpfheaders`,
            path: p`Out/headers/linux-x64`,
            trackSourceFileChanges: false,
            isWritable: true,
            isReadable: true,
            isScrubbable: true
        },
        ...(Environment.getStringValue("BUILDXL_DROP_CONFIG") !== undefined ? 
        [
            {
                // Path used in CloudBuild for things like drop configuration files. These files should not be tracked.
                name: a`CloudBuild`,
                path: Environment.getPathValue("BUILDXL_DROP_CONFIG").parent,
                trackSourceFileChanges: false,
                isWritable: false,
                isReadable: true
            }
        ] : []),
        {
            name: a`ThirdParty_mono`,
            path: p`third_party/mono@abad3612068e7333956106e7be02d9ce9e346f92`,
            trackSourceFileChanges: true,
            isWritable: false,
            isReadable: true
        },
        ...(Environment.hasVariable("TOOLPATH_GUARDIAN") ? 
        [
            {
                name: a`GuardianDrop`,
                path: Environment.getPathValue("TOOLPATH_GUARDIAN").parent,
                isReadable: true,
                isWritable: true,
                trackSourceFileChanges: true
            }
        ] : []),
        ...(Environment.hasVariable("ESRP_POLICY_CONFIG") ?
        [
            {
                name: a`EsrpPolicyConfig`,
                path: Environment.getPathValue("ESRP_POLICY_CONFIG").parent,
                isReadable: true,
                isWritable: false,
                trackSourceFileChanges: true
            }
        ] : []),
        ...(Environment.hasVariable("ESRP_SESSION_CONFIG") ? 
        [
            { 
                name: a`EsrpSessionConfig`,
                path: Environment.getPathValue("ESRP_SESSION_CONFIG").parent,
                isReadable: true,
                isWritable: false,
                trackSourceFileChanges: true
            }
        ] : []),
        ...(Environment.getFlag("ENABLE_ESRP") && Environment.hasVariable("SIGN_TOOL_PATH") ? 
        [
            { 
                name: a`EsrpTool`,
                path: p`${Environment.expandEnvironmentVariablesInString(Environment.getStringValue("SIGN_TOOL_PATH"))}`.parent,
                isReadable: true,
                isWritable: false,
                trackSourceFileChanges: true
            }
        ] : []),
        // CODESYNC: Public/Src/Sandbox/Linux/ebpf/deployment.dsc
        ...(Environment.hasVariable("BuildXLEbpfSandboxDeploymentOverridePath") ?
        [
            // Contains files that might be copied over during the build.
            {
                name: a`BuildXLEbpfSandboxDeploymentOverrideDirectory`,
                path: Environment.getPathValue("BuildXLEbpfSandboxDeploymentOverridePath"),
                isReadable: true,
                isWritable: false,
                trackSourceFileChanges: true
            }
        ] : []),
    ],

    searchPathEnumerationTools: [
        r`cl.exe`,
        r`lib.exe`,
        r`link.exe`,
        r`sn.exe`,
        r`csc.exe`,
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

    cacheableFileAccessAllowlist: Context.getCurrentHost().os !== "win" ? [] : [
        // Allow the debugger to be able to be launched from BuildXL Builds
        {
            name: "JitDebugger",
            toolPath: f`${Environment.getDirectoryValue("SystemRoot")}/system32/vsjitdebugger.exe`,
            pathRegex: `.*${Environment.getStringValue("CommonProgramFiles").replace("\\", "\\\\")}\\\\Microsoft Shared\\\\VS7Debug\\\\.*`
        },
        // cl.exe may write temporary files under its working directory
        {
            name: "cl.exe",
            toolPath: a`cl.exe`,
            pathRegex: ".*.tmp"
        }
    ]
});
