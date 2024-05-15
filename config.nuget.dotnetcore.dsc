// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const coreVersion = "3.1.0";
const core50Version = "5.0.0";
const core60Version = "6.0.30";
const core80Version = "8.0.5";

// Microsoft.NETCore.Platforms has become out of sync with the rest of the packages that use core60Version
// Updaters of this file might want to try to restore the sync: for now we are using the latest version we can
const core60VersionPlatforms = "6.0.11"; 
const core80VersionPlatforms = "8.0.0-preview.7.23375.6";

const pkgVersion = "4.3.0";
const pkgVersionNext = "4.7.0";
const pkgVersion5 = "5.0.0";
const pkgVersion6 = "6.0.0";
const pkgVersion6Preview = "6.0.0-preview.5.21301.5";

export const pkgs = [

    // .NET Core Dependencies
    { id: "Microsoft.NETCore.App.Ref", version: coreVersion },

    { id: "NETStandard.Library", version: "2.0.3", tfm: ".NETStandard2.0" },
    { id: "Microsoft.NETCore.Platforms", version: core50Version },
    
    // .NET Core Self-Contained Deployment
    { id: "Microsoft.NETCore.DotNetHostResolver", version: coreVersion },

    { id: "Microsoft.NETCore.DotNetHostPolicy", version: coreVersion },

    { id: "Microsoft.NETCore.DotNetAppHost", version: coreVersion },

    // .NET 6

    // .NET Core 6.0 Dependencies
    { id: "Microsoft.NETCore.App.Ref", version: core60Version, alias: "Microsoft.NETCore.App.Ref60" },

   
    // .NET Core Self-Contained Deployment
    { id: "Microsoft.NETCore.DotNetHostResolver", version: core60Version, alias: "Microsoft.NETCore.DotNetHostResolver.6.0" },

    { id: "Microsoft.NETCore.DotNetHostPolicy", version: core60Version, alias: "Microsoft.NETCore.DotNetHostPolicy.6.0" },

    { id: "Microsoft.NETCore.DotNetAppHost", version: core60Version, alias: "Microsoft.NETCore.DotNetAppHost.6.0" },

    // .NET Core win-x64 runtime deps
    { id: "Microsoft.NETCore.App.Host.win-x64", version: core60Version, osSkip: [ "macOS", "unix" ], alias: "Microsoft.NETCore.App.Host.win-x64.6.0" },
    { id: "Microsoft.NETCore.App.Runtime.win-x64", version: core60Version, osSkip: [ "macOS", "unix" ], alias: "Microsoft.NETCore.App.Runtime.win-x64.6.0" },
    { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver", version: core60Version, osSkip: [ "macOS", "unix" ], alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver.6.0" },
    { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy", version: core60Version, osSkip: [ "macOS", "unix" ], alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy.6.0" },

    // .NET Core osx-x64 runtime deps
    { id: "Microsoft.NETCore.App.Host.osx-x64", version: core60Version, alias: "Microsoft.NETCore.App.Host.osx-x64.6.0" },
    { id: "Microsoft.NETCore.App.Runtime.osx-x64", version: core60Version, alias: "Microsoft.NETCore.App.Runtime.osx-x64.6.0"},
    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver", version: core60Version, alias: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver.6.0" },
    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy", version: core60Version, alias: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy.6.0" },

    // .NET Core linux-x64 runtime deps
    { id: "Microsoft.NETCore.App.Runtime.linux-x64", version: core60Version, alias: "Microsoft.NETCore.App.Runtime.linux-x64.6.0" },
    { id: "Microsoft.NETCore.App.Host.linux-x64", version: core60Version, alias: "Microsoft.NETCore.App.Host.linux-x64.6.0" },
    { id: "runtime.linux-x64.Microsoft.NETCore.DotNetHostResolver", version: core60Version, alias: "runtime.linux-x64.Microsoft.NETCore.DotNetHostResolver.6.0" },
    { id: "runtime.linux-x64.Microsoft.NETCore.DotNetHostPolicy", version: core60Version, alias: "runtime.linux-x64.Microsoft.NETCore.DotNetHostPolicy.6.0" },    

    // .NET 8

    // .NET Core 8.0 Dependencies
    { id: "Microsoft.NETCore.App.Ref", version: core80Version, alias: "Microsoft.NETCore.App.Ref80",
        // This dll has a partial copy of System.Text.Json which causes collisions with real System.Text.Json
        filesToExclude: [r`analyzers/dotnet/cs/System.Text.Json.SourceGeneration.dll`] },

    { id: "Microsoft.NETCore.Platforms", version: core80VersionPlatforms, alias: "Microsoft.NETCore.Platforms.8.0" },
    
    // .NET Core Self-Contained Deployment
    { id: "Microsoft.NETCore.DotNetHostResolver", version: core80Version, alias: "Microsoft.NETCore.DotNetHostResolver.8.0" },

    { id: "Microsoft.NETCore.DotNetHostPolicy", version: core80Version, alias: "Microsoft.NETCore.DotNetHostPolicy.8.0" },

    { id: "Microsoft.NETCore.DotNetAppHost", version: core80Version, alias: "Microsoft.NETCore.DotNetAppHost.8.0" },

    // .NET Core win-x64 runtime deps
    { id: "Microsoft.NETCore.App.Host.win-x64", version: core80Version, osSkip: [ "macOS", "unix" ], alias: "Microsoft.NETCore.App.Host.win-x64.8.0" },
    { id: "Microsoft.NETCore.App.Runtime.win-x64", version: core80Version, osSkip: [ "macOS", "unix" ], alias: "Microsoft.NETCore.App.Runtime.win-x64.8.0" },
    { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver", version: core80Version, osSkip: [ "macOS", "unix" ], alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver.8.0" },
    { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy", version: core80Version, osSkip: [ "macOS", "unix" ], alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy.8.0" },

    // .NET Core osx-x64 runtime deps
    { id: "Microsoft.NETCore.App.Host.osx-x64", version: core80Version, alias: "Microsoft.NETCore.App.Host.osx-x64.8.0" },
    { id: "Microsoft.NETCore.App.Runtime.osx-x64", version: core80Version, alias: "Microsoft.NETCore.App.Runtime.osx-x64.8.0"},
    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver", version: core80Version, alias: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver.8.0" },
    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy", version: core80Version, alias: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy.8.0" },

    // .NET Core linux-x64 runtime deps
    { id: "Microsoft.NETCore.App.Runtime.linux-x64", version: core80Version, alias: "Microsoft.NETCore.App.Runtime.linux-x64.8.0" },
    { id: "Microsoft.NETCore.App.Host.linux-x64", version: core80Version, alias: "Microsoft.NETCore.App.Host.linux-x64.8.0" },
    { id: "runtime.linux-x64.Microsoft.NETCore.DotNetHostResolver", version: core80Version, alias: "runtime.linux-x64.Microsoft.NETCore.DotNetHostResolver.8.0" },
    { id: "runtime.linux-x64.Microsoft.NETCore.DotNetHostPolicy", version: core80Version, alias: "runtime.linux-x64.Microsoft.NETCore.DotNetHostPolicy.8.0" },

    { id: "runtime.native.System", version: pkgVersion },
    { id: "runtime.win7-x64.runtime.native.System.Data.SqlClient.sni", version: pkgVersion, osSkip: [ "macOS", "unix" ] },
    { id: "runtime.win7-x86.runtime.native.System.Data.SqlClient.sni", version: pkgVersion, osSkip: [ "macOS", "unix" ] },
    { id: "runtime.native.System.Data.SqlClient.sni", version: pkgVersion },
    { id: "runtime.native.System.Net.Http", version: pkgVersion },
    { id: "runtime.native.System.IO.Compression", version: pkgVersion },
    { id: "runtime.native.System.Net.Security", version: pkgVersion },
    { id: "runtime.native.System.Security.Cryptography.Apple", version: pkgVersion },
    { id: "runtime.osx.10.10-x64.runtime.native.System.Security.Cryptography.Apple", version: pkgVersion },
    { id: "runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.debian.8-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.fedora.23-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.fedora.24-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.opensuse.13.2-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.opensuse.42.1-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.osx.10.10-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.rhel.7-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.ubuntu.14.04-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.ubuntu.16.04-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.ubuntu.16.10-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },

    // Packages
    { id: "Microsoft.CSharp", version: pkgVersion },
    { id: "Microsoft.Win32.Primitives", version: pkgVersion },
    { id: "Microsoft.Win32.Registry", version: "4.7.0" }, // This is the version our dependencies rely on.
    { id: "System.AppContext", version: pkgVersion },
    { id: "System.Collections", version: pkgVersion },
    { id: "System.Collections.Concurrent", version: pkgVersion },
    { id: "System.Collections.NonGeneric", version: pkgVersion },
    { id: "System.Collections.Specialized", version: pkgVersion },
    { id: "System.ComponentModel", version: pkgVersion },
    { id: "System.ComponentModel.Annotations", version: pkgVersion },
    { id: "System.ComponentModel.Composition", version: "4.5.0" },
    { id: "System.ComponentModel.EventBasedAsync", version: pkgVersion },
    { id: "System.ComponentModel.Primitives", version: pkgVersion },
    { id: "System.ComponentModel.TypeConverter", version: pkgVersion },
    { id: "System.Console", version: pkgVersion },
    { id: "System.Data.Common", version: pkgVersion },
    { id: "System.Data.SqlClient", version: "4.8.6" },
    { id: "System.Diagnostics.Contracts", version: pkgVersion },
    { id: "System.Diagnostics.Debug", version: pkgVersion },
    { id: "System.Diagnostics.FileVersionInfo", version: pkgVersion },
    { id: "System.Diagnostics.Process", version: pkgVersion },
    { id: "System.Diagnostics.StackTrace", version: pkgVersion },
    { id: "System.Diagnostics.TextWriterTraceListener", version: pkgVersion },
    { id: "System.Diagnostics.Tools", version: pkgVersion },
    { id: "System.Diagnostics.TraceSource", version: pkgVersion },
    { id: "System.Diagnostics.Tracing", version: pkgVersion },
    { id: "System.Drawing.Primitives", version: pkgVersion },
    { id: "System.Dynamic.Runtime", version: pkgVersion },
    { id: "System.Globalization", version: pkgVersion },
    { id: "System.Globalization.Calendars", version: pkgVersion },
    { id: "System.Globalization.Extensions", version: pkgVersion },
    { id: "System.IO", version: pkgVersion },
    { id: "System.IO.Compression", version: pkgVersion },
    { id: "System.IO.Compression.ZipFile", version: pkgVersion },
    { id: "System.IO.FileSystem", version: pkgVersion },
    { id: "System.IO.FileSystem.DriveInfo", version: pkgVersion },
    { id: "System.IO.FileSystem.Primitives", version: pkgVersion },
    { id: "System.IO.FileSystem.Watcher", version: pkgVersion },
    { id: "System.IO.IsolatedStorage", version: pkgVersion },
    { id: "System.IO.MemoryMappedFiles", version: pkgVersion },
    { id: "System.IO.Pipes", version: pkgVersion },
    { id: "System.IO.Pipes.AccessControl", version: pkgVersion },
    { id: "System.IO.UnmanagedMemoryStream", version: pkgVersion },
    { id: "System.Linq", version: pkgVersion },
    { id: "System.Linq.Expressions", version: pkgVersion },
    { id: "System.Linq.Parallel", version: pkgVersion },
    { id: "System.Linq.Queryable", version: pkgVersion },
    { id: "System.Net.Http", version: pkgVersion },
    { id: "System.Net.NameResolution", version: pkgVersion },
    { id: "System.Net.NetworkInformation", version: pkgVersion },
    { id: "System.Net.Ping", version: pkgVersion },
    { id: "System.Net.Primitives", version: pkgVersion },
    { id: "System.Net.Requests", version: pkgVersion },
    { id: "System.Net.Security", version: "4.3.1" },
    { id: "System.Net.Sockets", version: pkgVersion },
    { id: "System.Net.WebHeaderCollection", version: pkgVersion },
    { id: "System.Net.WebSockets", version: pkgVersion },
    { id: "System.Net.WebSockets.Client", version: "4.3.1" },
    { id: "System.ObjectModel", version: pkgVersion },
    { id: "System.Private.DataContractSerialization", version: pkgVersion },
    { id: "System.Reflection", version: pkgVersion },
    { id: "System.Reflection.DispatchProxy", version: pkgVersion },
    { id: "System.Reflection.Emit", version: pkgVersion },
    { id: "System.Reflection.Emit.ILGeneration", version: pkgVersion },
    { id: "System.Reflection.Emit.Lightweight", version: pkgVersion },
    { id: "System.Reflection.Extensions", version: pkgVersion },
    { id: "System.Reflection.Primitives", version: pkgVersion },
    { id: "System.Reflection.TypeExtensions", version: pkgVersion },
    { id: "System.Resources.Reader", version: pkgVersion },
    { id: "System.Resources.ResourceManager", version: pkgVersion },
    { id: "System.Resources.Writer", version: pkgVersion },
    { id: "System.Runtime", version: pkgVersion },
    { id: "System.Runtime.CompilerServices.VisualC", version: pkgVersion },
    { id: "System.Runtime.Extensions", version: pkgVersion },
    { id: "System.Runtime.Handles", version: pkgVersion },
    { id: "System.Runtime.InteropServices", version: pkgVersion },
    { id: "System.Runtime.InteropServices.RuntimeInformation", version: pkgVersion },
    { id: "System.Runtime.InteropServices.WindowsRuntime", version: pkgVersion },
    { id: "System.Runtime.Loader", version: pkgVersion },
    { id: "System.Runtime.Numerics", version: pkgVersion },
    { id: "System.Runtime.Serialization.Formatters", version: pkgVersion },
    { id: "System.Runtime.Serialization.Json", version: pkgVersion },
    { id: "System.Runtime.Serialization.Primitives", version: pkgVersion },
    { id: "System.Runtime.Serialization.Xml", version: pkgVersion },
    { id: "System.Runtime.WindowsRuntime", version: pkgVersion },
    { id: "System.Security.Cryptography.Algorithms", version: pkgVersion },
    { id: "System.Security.Cryptography.Cng", version: pkgVersion5 },
    { id: "System.Security.Cryptography.Csp", version: pkgVersion },
    { id: "System.Security.Cryptography.Encoding", version: pkgVersion },
    { id: "System.Security.Cryptography.Primitives", version: pkgVersion },
    { id: "System.Security.Cryptography.X509Certificates", version: pkgVersion },
    { id: "System.Security.Principal", version: pkgVersion },
    { id: "System.Security.SecureString", version: pkgVersion },
    { id: "System.Security.Claims", version: pkgVersion },
    { id: "System.Text.Encoding", version: pkgVersion },
    { id: "System.Text.Encoding.Extensions", version: pkgVersion },
    { id: "System.Text.RegularExpressions", version: pkgVersion },
    { id: "System.Threading", version: pkgVersion },
    { id: "System.Threading.Overlapped", version: pkgVersion },
    { id: "System.Threading.Tasks", version: pkgVersion },
    { id: "System.Formats.Asn1", version: "7.0.0"},
    
    { id: "System.Threading.Tasks.Parallel", version: pkgVersion },
    { id: "System.Threading.Thread", version: pkgVersion },
    { id: "System.Threading.ThreadPool", version: pkgVersion },
    { id: "System.Threading.Timer", version: pkgVersion },
    { id: "System.ValueTuple", version: pkgVersion },
    { id: "System.Xml.ReaderWriter", version: pkgVersion },
    { id: "System.Xml.XDocument", version: pkgVersion },
    { id: "System.Xml.XmlDocument", version: pkgVersion },
    { id: "System.Xml.XmlSerializer", version: pkgVersion },
    { id: "System.Xml.XPath", version: pkgVersion },
    { id: "System.Xml.XPath.XDocument", version: pkgVersion },
    { id: "System.Xml.XPath.XmlDocument", version: pkgVersion },

    // Bumped version numbers
    { id: "System.IO.FileSystem.AccessControl", version: pkgVersionNext },
    { id: "System.Management", version: pkgVersionNext },
    { id: "System.Security.AccessControl", version: pkgVersion6 },
    { id: "System.Security.Principal.Windows", version: pkgVersion6Preview },
    
    { id: "System.Text.Json", version: "8.0.3" },
    { id: "System.Threading.AccessControl", version: pkgVersionNext },

    // Non-standard version ones
    { id: "Microsoft.NETCore.Targets", version: "2.0.0" },
    
    { id: "System.Threading.Tasks.Extensions", version: "4.5.4" }, // If you change this version, please change cacheBindingRedirects in BuildXLSdk.dsc

    { id: "System.Security.Cryptography.OpenSsl", version: "4.4.0" },
    { id: "System.Collections.Immutable", version: "8.0.0" },
];
