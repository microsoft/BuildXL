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
                hash: "17E8C8C0CDCCA3A6D1EE49836847148C4623ACEA5E6E36E10B691DA7FDC4C39200",
            },

            repositories: importFile(f`config.microsoftInternal.dsc`).isMicrosoftInternal
                ? {
                    "BuildXL.Selfhost": "https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json",
                    // Note: From a compliance point of view it is important that MicrosoftInternal has a single feed.
                    // If you need to consume packages make sure they are upstreamed in that feed.
                  }
                : {
                    "buildxl-selfhost" : "https://dotnet.myget.org/F/buildxl-selfhost/api/v3/index.json",
                    "nuget.org" : "http://api.nuget.org/v3/index.json",
                    "roslyn-tools" : "https://dotnet.myget.org/F/roslyn-tools/api/v3/index.json",
                    "msbuild" : "https://dotnet.myget.org/F/msbuild/api/v3/index.json",
                    "dotnet-core" : "https://dotnet.myget.org/F/dotnet-core/api/v3/index.json",
                    "dotnet-arcade" : "https://dotnetfeed.blob.core.windows.net/dotnet-core/index.json",
                  },

            packages: [
                { id: "Bond.Core.CSharp", version: "8.0.0" },
                { id: "Bond.CSharp", version: "8.0.0" },
                { id: "Bond.CSharp.osx-x64", version: "8.0.0" },
                { id: "Bond.Runtime.CSharp", version: "8.0.0" },
                { id: "CLAP", version: "4.6" },

                { id: "RuntimeContracts", version: "0.1.7.1" },

                { id: "Microsoft.NETFramework.ReferenceAssemblies.net451", version: "1.0.0-alpha-5"},
                { id: "Microsoft.NETFramework.ReferenceAssemblies.net461", version: "1.0.0-alpha-5"},
                { id: "Microsoft.NETFramework.ReferenceAssemblies.net472", version: "1.0.0-alpha-5"},

                { id: "EntityFramework", version: "6.0.0" },

                { id: "System.Diagnostics.DiagnosticSource", version: "4.5.0" },
                { id: "System.Diagnostics.DiagnosticSource", version: "4.0.0-beta-23516", alias: "System.Diagnostics.DiagnosticsSource.ForEventHub"},

                // Roslyn
                { id: "Microsoft.Net.Compilers", version: "3.0.0" },
                { id: "Microsoft.NETCore.Compilers", version: "3.1.0-beta3-final" },
                { id: "Microsoft.CodeAnalysis.Common", version: "2.10.0" },
                { id: "Microsoft.CodeAnalysis.CSharp", version: "2.10.0" },
                { id: "Microsoft.CodeAnalysis.VisualBasic", version: "2.10.0" },
                { id: "Microsoft.CodeAnalysis.Workspaces.Common", version: "2.10.0",
                    dependentPackageIdsToSkip: ["SQLitePCLRaw.bundle_green", "System.Composition"],
                    dependentPackageIdsToIgnore: ["SQLitePCLRaw.bundle_green", "System.Composition"],
                },
                { id: "Microsoft.CodeAnalysis.CSharp.Workspaces", version: "2.10.0" },
                { id: "Microsoft.CodeAnalysis.VisualBasic.Workspaces", version: "2.10.0" },

                // Roslyn Analyzers
                { id: "Microsoft.CodeAnalysis.Analyzers", version: "2.6.3" },
                { id: "Microsoft.CodeAnalysis.FxCopAnalyzers", version: "2.6.3" },
                { id: "Microsoft.CodeQuality.Analyzers", version: "2.3.0-beta1" },
                { id: "Microsoft.NetFramework.Analyzers", version: "2.3.0-beta1" },
                { id: "Microsoft.NetCore.Analyzers", version: "2.3.0-beta1" },
                { id: "AsyncFixer", version: "1.1.5" },
                { id: "ErrorProne.NET.CoreAnalyzers", version: "0.1.2" },
                { id: "RuntimeContracts.Analyzer", version: "0.1.7.1" },
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

                { id: "Microsoft.Tpl.Dataflow", version: "4.5.24" },
                { id: "Microsoft.TypeScript.Compiler", version: "1.8" },
                { id: "Microsoft.WindowsAzure.ConfigurationManager", version: "1.8.0.0" },
                { id: "Newtonsoft.Json", version: "11.0.2" },
                { id: "Newtonsoft.Json", version: "10.0.3", alias: "Newtonsoft.Json.v10" },
                { id: "Newtonsoft.Json.Bson", version: "1.0.1" },
                { id: "System.Data.SQLite", version: "1.0.109.2" },
                { id: "System.Data.SQLite.Core", version: "1.0.109.2" },
                { id: "System.Data.SQLite.EF6", version: "1.0.102.0" },
                { id: "System.Data.SQLite.Linq", version: "1.0.102.0" },
                { id: "System.Reflection.Metadata", version: "1.6.0" },
                { id: "System.Threading.Tasks.Dataflow", version: "4.9.0" },
                { id: "System.Threading.Tasks.Dataflow", version: "4.5.24", alias: "DataflowForMSBuildRuntime"},

                // Nuget
                { id: "NuGet.Commandline", version: "4.7.1" },
                { id: "NuGet.Versioning", version: "4.6.0" }, // Can't use the latest becuase nuget extracts to folder with metadata which we don't support yet.

                // Cpp Sdk
                { id: "VisualCppTools.Community.VS2017Layout", version: "14.11.25506"},

                // ProjFS (virtual file system)
                { id: "Microsoft.Windows.ProjFS", version: "1.0.19079.1" },

                // RocksDb
                { id: "RocksDbSharp", version: "5.8.0-b20181023.3", alias: "RocksDbSharpSigned" },
                { id: "RocksDbNative", version: "6.0.1-b20190426.4" },

                { id: "JsonDiffPatch.Net", version: "2.1.0" },

                // Event hubs
                { id: "Microsoft.Azure.Amqp", version: "2.3.5" },
                { id: "Microsoft.Azure.EventHubs", version: "2.1.0",
                    dependentPackageIdsToSkip: ["System.Net.Http", "System.Reflection.TypeExtensions", "System.Runtime.Serialization.Primitives", "Newtonsoft.Json", "System.Diagnostics.DiagnosticSource"],
                    dependentPackageIdsToIgnore: ["System.Net.Http", "System.Reflection.TypeExtensions", "System.Runtime.Serialization.Primitives", "Newtonsoft.Json", "System.Diagnostics.DiagnosticSource"],
                },
                { id: "Microsoft.Azure.KeyVault.Core", version: "1.0.0" },
                { id: "Microsoft.Azure.Services.AppAuthentication", version: "1.0.3" },
                { id: "Microsoft.IdentityModel.Logging", version: "5.2.2" },
                { id: "Microsoft.IdentityModel.Tokens", version: "5.2.2", dependentPackageIdsToSkip: ["Newtonsoft.Json"] },
                { id: "System.IdentityModel.Tokens.Jwt", version: "5.2.2", dependentPackageIdsToSkip: ["Newtonsoft.Json"] },

                // Package sets
                ...importFile(f`config.nuget.vssdk.dsc`).pkgs,
                ...importFile(f`config.nuget.aspNetCore.dsc`).pkgs,
                ...importFile(f`config.microsoftInternal.dsc`).pkgs,

                { id: "WindowsAzure.Storage", version: "9.3.3", alias: "WindowsAzure.Storage" },
                { id: "Microsoft.Data.OData", version: "5.8.2" },
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
                { id: "xunit.runner.visualstudio", version: "2.4.1" },

                { id: "Microsoft.IdentityModel.Clients.ActiveDirectory", version: "4.5.1" },

                // CloudStore dependencies
                { id: "Microsoft.Bcl", version: "1.1.10" },
                { id: "Microsoft.Bcl.Async", version: "1.0.168" },
                { id: "Microsoft.Bcl.Build", version: "1.0.14" },
                { id: "StackExchange.Redis.StrongName", version: "1.2.6" },
                { id: "System.Interactive.Async", version: "3.1.1" },
                { id: "TransientFaultHandling.Core", version: "5.1.1209.1" },
                { id: "Grpc", version: "1.18.0" },
                { id: "Grpc.Core", version: "1.18.0" },
                { id: "Grpc.Tools", version: "1.18.0" },
                { id: "Google.Protobuf", version: "3.7.0" },
                { id: "Redis-64", version: "3.0.503" },

                // Testing
                { id: "System.Security.Cryptography.ProtectedData", version: "4.4.0"},
                { id: "System.Configuration.ConfigurationManager", version: "4.4.0"},
                { id: "FluentAssertions", version: "5.3.0", dependentPackageIdsToSkip: ["System.Reflection.Emit", "System.Reflection.Emit.Lightweight"] },

                { id: "DotNet.Glob", version: "2.0.3" },
                { id: "Minimatch", version: "1.1.0.0" },
                { id: "Microsoft.ApplicationInsights", version: "2.3.0" },
                { id: "Microsoft.ApplicationInsights.Agent.Intercept", version: "2.0.7" },
                { id: "Microsoft.ApplicationInsights.DependencyCollector", version: "2.3.0" },
                { id: "Microsoft.ApplicationInsights.PerfCounterCollector", version: "2.3.0" },
                { id: "Microsoft.ApplicationInsights.WindowsServer", version: "2.3.0" },
                { id: "Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel", version: "2.3.0" },
                { id: "System.Memory", version: "4.5.1" },
                { id: "System.Runtime.CompilerServices.Unsafe", version: "4.5.0" },
                { id: "System.IO.Pipelines", version: "4.5.2" },
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

                // .NET Core Dependencies
                { id: "Microsoft.NETCore.App", version: "3.0.0-preview5-27626-15" },
                { id: "Microsoft.NETCore.App", version: "2.1.1", alias: "Microsoft.NETCore.App.211" },

                { id: "NETStandard.Library", version: "2.0.3", tfm: ".NETStandard2.0" },
                { id: "Microsoft.NETCore.Platforms", version: "3.0.0-preview5.19224.8" },
                { id: "Microsoft.NETCore.DotNetHostPolicy", version: "3.0.0-preview5-27626-15"},
                { id: "System.Security.Claims", version: "4.3.0" },

                // .NET Core Self-Contained Deployment
                { id: "Microsoft.NETCore.DotNetHostResolver", version: "3.0.0-preview5-27626-15" },
                { id: "Microsoft.NETCore.DotNetHostResolver", version: "2.2.0", alias: "Microsoft.NETCore.DotNetHostResolver.220" },

                { id: "Microsoft.NETCore.DotNetAppHost", version: "3.0.0-preview5-27626-15" },
                { id: "Microsoft.NETCore.DotNetAppHost", version: "2.2.0", alias: "Microsoft.NETCore.DotNetAppHost.220" },

                // .NET Core win-x64 runtime deps
                { id: "runtime.win-x64.Microsoft.NETCore.DotNetAppHost", version: "3.0.0-preview5-27626-15" },
                { id: "runtime.win-x64.Microsoft.NETCore.DotNetAppHost", version: "2.2.0", alias: "runtime.win-x64.Microsoft.NETCore.DotNetAppHost.220" },

                { id: "runtime.win-x64.Microsoft.NETCore.App", version: "3.0.0-preview5-27626-15" },
                { id: "runtime.win-x64.Microsoft.NETCore.App", version: "2.2.0", alias: "runtime.win-x64.Microsoft.NETCore.App.220" },

                { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver", version: "3.0.0-preview5-27626-15" },
                { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver", version: "2.2.0", alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver.220" },

                { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy", version: "3.0.0-preview5-27626-15" },
                { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy", version: "2.2.0", alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy.220" },

                // .NET Core osx-x64 runtime deps
                { id: "runtime.osx-x64.Microsoft.NETCore.DotNetAppHost", version: "3.0.0-preview5-27626-15" },
                { id: "runtime.osx-x64.Microsoft.NETCore.DotNetAppHost", version: "2.2.0", alias: "runtime.osx-x64.Microsoft.NETCore.DotNetAppHost.220" },

                { id: "runtime.osx-x64.Microsoft.NETCore.App", version: "3.0.0-preview5-27626-15" },
                { id: "runtime.osx-x64.Microsoft.NETCore.App", version: "2.2.0", alias: "runtime.osx-x64.Microsoft.NETCore.App.220" },

                { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver", version: "3.0.0-preview5-27626-15" },
                { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver", version: "2.2.0", alias: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver.220" },

                { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy", version: "3.0.0-preview5-27626-15" },
                { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy", version: "2.2.0", alias: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy.220" },

                // DotNetCore related deps
                { id: "Microsoft.CSharp", version: "4.3.0" },
                { id: "Microsoft.Win32.Primitives", version: "4.3.0" },
                { id: "Microsoft.Win32.Registry", version: "4.3.0" },
                { id: "System.AppContext", version: "4.3.0" },
                { id: "System.Buffers", version: "4.3.0" },
                { id: "System.Collections", version: "4.3.0" },
                { id: "System.Collections.Concurrent", version: "4.3.0" },
                { id: "System.Collections.NonGeneric", version: "4.3.0" },
                { id: "System.Collections.Specialized", version: "4.3.0" },
                { id: "System.ComponentModel", version: "4.3.0" },
                { id: "System.ComponentModel.Annotations", version: "4.3.0" },
                { id: "System.ComponentModel.Composition", version: "4.5.0" },
                { id: "System.ComponentModel.EventBasedAsync", version: "4.3.0" },
                { id: "System.ComponentModel.Primitives", version: "4.3.0" },
                { id: "System.ComponentModel.TypeConverter", version: "4.3.0" },
                { id: "System.Console", version: "4.3.0" },
                { id: "System.Data.Common", version: "4.3.0" },
                { id: "System.Data.SqlClient", version: "4.3.0" },
                { id: "System.Diagnostics.Contracts", version: "4.3.0" },
                { id: "System.Diagnostics.Debug", version: "4.3.0" },
                { id: "System.Diagnostics.FileVersionInfo", version: "4.3.0" },
                { id: "System.Diagnostics.Process", version: "4.3.0" },
                { id: "System.Diagnostics.StackTrace", version: "4.3.0" },
                { id: "System.Diagnostics.TextWriterTraceListener", version: "4.3.0" },
                { id: "System.Diagnostics.Tools", version: "4.3.0" },
                { id: "System.Diagnostics.TraceSource", version: "4.3.0" },
                { id: "System.Diagnostics.Tracing", version: "4.3.0" },
                { id: "System.Drawing.Primitives", version: "4.3.0" },
                { id: "System.Dynamic.Runtime", version: "4.3.0" },
                { id: "System.Globalization", version: "4.3.0" },
                { id: "System.Globalization.Calendars", version: "4.3.0" },
                { id: "System.Globalization.Extensions", version: "4.3.0" },
                { id: "System.IO", version: "4.3.0" },
                { id: "System.IO.Compression", version: "4.3.0" },
                { id: "System.IO.Compression.ZipFile", version: "4.3.0" },
                { id: "System.IO.FileSystem", version: "4.3.0" },
                { id: "System.IO.FileSystem.AccessControl", version: "4.6.0-preview5.19224.8" },
                { id: "System.IO.FileSystem.DriveInfo", version: "4.3.0" },
                { id: "System.IO.FileSystem.Primitives", version: "4.3.0" },
                { id: "System.IO.FileSystem.Watcher", version: "4.3.0" },
                { id: "System.IO.IsolatedStorage", version: "4.3.0" },
                { id: "System.IO.MemoryMappedFiles", version: "4.3.0" },
                { id: "System.IO.Pipes", version: "4.3.0" },
                { id: "System.IO.Pipes.AccessControl", version: "4.3.0" },
                { id: "System.IO.UnmanagedMemoryStream", version: "4.3.0" },
                { id: "System.Linq", version: "4.3.0" },
                { id: "System.Linq.Expressions", version: "4.3.0" },
                { id: "System.Linq.Parallel", version: "4.3.0" },
                { id: "System.Linq.Queryable", version: "4.3.0" },
                { id: "System.Management", version: "4.6.0-preview5.19224.8" },
                { id: "System.Net.Http", version: "4.3.0" },
                { id: "System.Net.NameResolution", version: "4.3.0" },
                { id: "System.Net.NetworkInformation", version: "4.3.0" },
                { id: "System.Net.Ping", version: "4.3.0" },
                { id: "System.Net.Primitives", version: "4.3.0" },
                { id: "System.Net.Requests", version: "4.3.0" },
                { id: "System.Net.Security", version: "4.3.1" },
                { id: "System.Net.Sockets", version: "4.3.0" },
                { id: "System.Net.WebHeaderCollection", version: "4.3.0" },
                { id: "System.Net.WebSockets", version: "4.3.0" },
                { id: "System.Net.WebSockets.Client", version: "4.3.1" },
                { id: "System.Numerics.Vectors", version: "4.3.0" },
                { id: "System.ObjectModel", version: "4.3.0" },
                { id: "System.Private.DataContractSerialization", version: "4.3.0" },
                { id: "System.Reflection", version: "4.3.0" },
                { id: "System.Reflection.DispatchProxy", version: "4.3.0" },
                { id: "System.Reflection.Emit", version: "4.3.0" },
                { id: "System.Reflection.Emit.ILGeneration", version: "4.3.0" },
                { id: "System.Reflection.Emit.Lightweight", version: "4.3.0" },
                { id: "System.Reflection.Extensions", version: "4.3.0" },
                { id: "System.Reflection.Primitives", version: "4.3.0" },
                { id: "System.Reflection.TypeExtensions", version: "4.3.0" },
                { id: "System.Resources.Reader", version: "4.3.0" },
                { id: "System.Resources.ResourceManager", version: "4.3.0" },
                { id: "System.Resources.Writer", version: "4.3.0" },
                { id: "System.Runtime", version: "4.3.0" },
                { id: "System.Runtime.CompilerServices.VisualC", version: "4.3.0" },
                { id: "System.Runtime.Extensions", version: "4.3.0" },
                { id: "System.Runtime.Handles", version: "4.3.0" },
                { id: "System.Runtime.InteropServices", version: "4.3.0" },
                { id: "System.Runtime.InteropServices.RuntimeInformation", version: "4.3.0" },
                { id: "System.Runtime.InteropServices.WindowsRuntime", version: "4.3.0" },
                { id: "System.Runtime.Loader", version: "4.3.0" },
                { id: "System.Runtime.Numerics", version: "4.3.0" },
                { id: "System.Runtime.Serialization.Formatters", version: "4.3.0" },
                { id: "System.Runtime.Serialization.Json", version: "4.3.0" },
                { id: "System.Runtime.Serialization.Primitives", version: "4.3.0" },
                { id: "System.Runtime.Serialization.Xml", version: "4.3.0" },
                { id: "System.Security.AccessControl", version: "4.6.0-preview5.19224.8", dependentPackageIdsToSkip: ["System.Security.Principal.Windows"] },
                { id: "System.Security.Cryptography.Algorithms", version: "4.3.0" },
                { id: "System.Security.Cryptography.Cng", version: "4.3.0" },
                { id: "System.Security.Cryptography.Csp", version: "4.3.0" },
                { id: "System.Security.Cryptography.Encoding", version: "4.3.0" },
                { id: "System.Security.Cryptography.Primitives", version: "4.3.0" },
                { id: "System.Security.Cryptography.X509Certificates", version: "4.3.0" },
                { id: "System.Security.Principal", version: "4.3.0" },
                { id: "System.Security.Principal.Windows", version: "4.6.0-preview5.19224.8" },
                { id: "System.Security.SecureString", version: "4.3.0" },
                { id: "System.Text.Encoding", version: "4.3.0" },
                { id: "System.Text.Encoding.CodePages", version: "4.3.0" },
                { id: "System.Text.Encoding.Extensions", version: "4.3.0" },
                { id: "System.Text.RegularExpressions", version: "4.3.0" },
                { id: "System.Threading", version: "4.3.0" },
                { id: "System.Threading.AccessControl", version: "4.6.0-preview5.19224.8" },
                { id: "System.Threading.Overlapped", version: "4.3.0" },
                { id: "System.Threading.Tasks", version: "4.3.0" },
                { id: "System.Threading.Tasks.Extensions", version: "4.3.0" },
                { id: "System.Threading.Tasks.Parallel", version: "4.3.0" },
                { id: "System.Threading.Thread", version: "4.3.0" },
                { id: "System.Threading.ThreadPool", version: "4.3.0" },
                { id: "System.Threading.Timer", version: "4.3.0" },
                { id: "System.ValueTuple", version: "4.3.0" },
                { id: "System.Xml.ReaderWriter", version: "4.3.0" },
                { id: "System.Xml.XDocument", version: "4.3.0" },
                { id: "System.Xml.XmlDocument", version: "4.3.0" },
                { id: "System.Xml.XmlSerializer", version: "4.3.0" },
                { id: "System.Xml.XPath", version: "4.3.0" },
                { id: "System.Xml.XPath.XDocument", version: "4.3.0" },
                { id: "System.Xml.XPath.XmlDocument", version: "4.3.0" },

                // Non-standard version ones
                { id: "Microsoft.NETCore.Targets", version: "2.0.0" },
                { id: "System.Security.Cryptography.OpenSsl", version: "4.4.0" },
                { id: "System.Collections.Immutable", version: "1.5.0" },

                { id: "runtime.native.System", version: "4.3.0" },
                { id: "runtime.win7-x64.runtime.native.System.Data.SqlClient.sni", version: "4.3.0" },
                { id: "runtime.win7-x86.runtime.native.System.Data.SqlClient.sni", version: "4.3.0" },
                { id: "runtime.native.System.Data.SqlClient.sni", version: "4.3.0" },
                { id: "runtime.native.System.Net.Http", version: "4.3.0" },
                { id: "runtime.native.System.IO.Compression", version: "4.3.0" },
                { id: "runtime.native.System.Net.Security", version: "4.3.0" },
                { id: "runtime.native.System.Security.Cryptography.Apple", version: "4.3.0" },
                { id: "runtime.osx.10.10-x64.runtime.native.System.Security.Cryptography.Apple", version: "4.3.0" },
                { id: "runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.debian.8-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.fedora.23-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.fedora.24-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.opensuse.13.2-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.opensuse.42.1-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.osx.10.10-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.rhel.7-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.ubuntu.14.04-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.ubuntu.16.04-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },
                { id: "runtime.ubuntu.16.10-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: "4.3.0" },

                // VSTS managed API
                { id: "Microsoft.TeamFoundationServer.Client", version: "15.122.1-preview"},
                { id: "Microsoft.TeamFoundation.DistributedTask.WebApi", version: "15.122.1-preview", dependentPackageIdsToSkip: ["*"] },
                { id: "Microsoft.TeamFoundation.DistributedTask.Common", version: "15.112.1"},
                { id: "Microsoft.TeamFoundation.DistributedTask.Common.Contracts", version: "16.137.0-preview"},

                // MSBuild. These should be used for compile references only, as at runtime one can only practically use MSBuilds from Visual Studio / dotnet CLI
                { id: "Microsoft.Build.Runtime", version: "16.1.0-preview.42",
                    dependentPackageIdsToSkip: ["System.Threading.Tasks.Dataflow"],
                    dependentPackageIdsToIgnore: ["System.Threading.Tasks.Dataflow"],
                },
                { id: "Microsoft.Build.Tasks.Core", version: "16.1.0-preview.42",
                    dependentPackageIdsToSkip: ["System.Threading.Tasks.Dataflow"],
                    dependentPackageIdsToIgnore: ["System.Threading.Tasks.Dataflow"],
                },
                { id: "Microsoft.Build.Utilities.Core", version: "16.1.0-preview.42"},
                { id: "Microsoft.Build", version: "16.1.0-preview.42",
                    dependentPackageIdsToSkip: ["System.Threading.Tasks.Dataflow"],
                    dependentPackageIdsToIgnore: ["System.Threading.Tasks.Dataflow"],
                },

                { id: "Microsoft.Build.Framework", version: "16.1.0-preview.42"},

                // Extra dependencies to make MSBuild work
                { id: "Microsoft.VisualStudio.Setup.Configuration.Interop", version: "1.16.30"},
                { id: "System.CodeDom", version: "4.4.0"},

                // Used for MSBuild input/output prediction
                { id: "Microsoft.Build.Prediction", version: "0.2.0" },

                { id: "SharpZipLib", version: "1.1.0" },

                // Ninja JSON graph generation helper
                { id: "BuildXL.Tools.Ninjson", version: "0.0.6" },
                { id: "BuildXL.Tools.AppHostPatcher", version: "1.0.0" },

                // CoreRT
                { id: "runtime.osx-x64.Microsoft.DotNet.ILCompiler", version: "1.0.0-alpha-27527-01" },
                { id: "runtime.win-x64.Microsoft.DotNet.ILCompiler", version: "1.0.0-alpha-27527-01" },

                // Kusto SDK (for netstandard)
                { id: "Microsoft.Azure.Kusto.Cloud.Platform.Azure.NETStandard", version: "6.1.8", dependentPackageIdsToIgnore: ["Microsoft.Extensions.PlatformAbstractions"] },
                { id: "Microsoft.Azure.Kusto.Cloud.Platform.NETStandard", version: "6.1.8", dependentPackageIdsToIgnore: ["Microsoft.Extensions.PlatformAbstractions"] },
                { id: "Microsoft.Azure.Kusto.Data.NETStandard", version: "6.1.8", dependentPackageIdsToIgnore: ["Microsoft.Extensions.PlatformAbstractions"] },
                { id: "Microsoft.Azure.Kusto.Ingest.NETStandard", version: "6.1.8", dependentPackageIdsToIgnore: ["Microsoft.Extensions.PlatformAbstractions"] },
                { id: "Microsoft.IO.RecyclableMemoryStream", version: "1.2.2" },
                { id: "Microsoft.Azure.KeyVault", version: "3.0.1"},
                { id: "Microsoft.Azure.KeyVault.WebKey", version: "3.0.1"},
                { id: "Microsoft.Rest.ClientRuntime", version: "3.0.0", dependentPackageIdsToIgnore: ["Microsoft.NETCore.Runtime"]  },
                { id: "Microsoft.Rest.ClientRuntime.Azure", version: "3.3.18" },
                { id: "Microsoft.NETCore.Windows.ApiSets", version: "1.0.1" },

                // Kusto SDK (for full framework)
                { id: "Microsoft.Azure.Kusto.Data", version: "6.1.8" },
                { id: "Microsoft.Azure.Kusto.Ingest", version: "6.1.8" },
                { id: "Microsoft.Azure.Kusto.Tools", version: "2.2.2" },
                { id: "Microsoft.Azure.Management.Kusto", version: "1.0.0" },
            ],

            doNotEnforceDependencyVersions: true,
        },

        importFile(f`config.microsoftInternal.dsc`).resolver,

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

                // DotNet Core Runtime
                {
                    moduleName: "DotNet-Runtime.win-x64",
                    url: "https://download.visualstudio.microsoft.com/download/pr/9459ede1-e223-40c7-a4c5-2409e789121a/46d4eb6067bda9f412a472f7286ffd94/dotnet-runtime-3.0.0-preview5-27626-15-win-x64.zip",
                    hash: "VSO0:6DBFE7BC9FA24D33A46A3A0732164BD5A4F5984E8FCE091D305FA635CD876AA700",
                    archiveType: "zip",
                },
                {
                    moduleName: "DotNet-Runtime.osx-x64",
                    url: "https://download.visualstudio.microsoft.com/download/pr/85024962-5dee-4f64-ab29-a903f3749f85/6178bfacc58f4d9a596b5e3facc767ab/dotnet-runtime-3.0.0-preview5-27626-15-osx-x64.tar.gz",
                    hash: "VSO0:C6AB5808D30BFF857263BC467FE8D818F35486763F673F79CA5A758727CEF3A900",
                    archiveType: "tgz",
                },
                {
                    moduleName: "DotNet-Runtime.linux-x64",
                    url: "https://download.visualstudio.microsoft.com/download/pr/f15ad9ab-7bd2-4ff5-87b6-b1a08f062ea2/6fdd314c16c17ba22934cd0ac6b4d343/dotnet-runtime-3.0.0-preview5-27626-15-linux-x64.tar.gz",
                    hash: "VSO0:C6AB5808D30BFF857263BC467FE8D818F35486763F673F79CA5A758727CEF3A900",
                    archiveType: "tgz",
                },

                // NodeJs
                {
                    moduleName: "NodeJs.win-x64",
                    url: "https://nodejs.org/download/release/v8.12.0/node-v8.12.0-win-x64.zip",
                    hash: "VSO0:95276E5CC1A0F5095181114C16734E8E0416B222F232E257E31FEBF73324BC2300",
                    archiveType: "zip",
                },
                {
                    moduleName: "NodeJs.osx-x64",
                    url: "https://nodejs.org/download/release/v8.12.0/node-v8.12.0-darwin-x64.tar.gz",
                    hash: "VSO0:2D9315899B651CA8489F47580378C5C8EAE5E0DEB4F50AF5A149BEC7B387228000",
                    archiveType: "tgz",
                },
                {
                    moduleName: "NodeJs.linux-x64",
                    url: "https://nodejs.org/download/release/v8.12.0/node-v8.12.0-linux-arm64.tar.gz",
                    hash: "VSO0:9DE138F52CCCE4B89747BFDEC5D3A0DDBB23BF80BB2A45AE0218D852845AB13C00",
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
            targetFramework: "netcoreapp3.0",
            targetRuntime: Context.getCurrentHost().os === "win" ? "win-x64" : "osx-x64",
        },
        namedQualifiers: {
            Debug: {
                configuration: "debug",
                targetFramework: "netcoreapp3.0",
                targetRuntime: "win-x64",
            },
            DebugNet472: {
                configuration: "debug",
                targetFramework: "net472",
                targetRuntime: "win-x64",
            },
            DebugDotNetCore: {
                configuration: "debug",
                targetFramework: "netcoreapp3.0",
                targetRuntime: "win-x64",
            },
            DebugDotNetCoreMac: {
                configuration: "debug",
                targetFramework: "netcoreapp3.0",
                targetRuntime: "osx-x64",
            },

            // Release
            Release: {
                configuration: "release",
                targetFramework: "netcoreapp3.0",
                targetRuntime: "win-x64",
            },
            ReleaseNet472: {
                configuration: "release",
                targetFramework: "net472",
                targetRuntime: "win-x64",
            },

            ReleaseDotNetCore: {
                configuration: "release",
                targetFramework: "netcoreapp3.0",
                targetRuntime: "win-x64",
            },
            ReleaseDotNetCoreMac: {
                configuration: "release",
                targetFramework: "netcoreapp3.0",
                targetRuntime: "osx-x64",
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
