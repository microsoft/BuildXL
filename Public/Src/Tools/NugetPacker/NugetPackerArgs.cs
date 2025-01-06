// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using BuildXL.ToolSupport;

namespace Tool.Nuget.Packer
{
    /// <summary>
    /// Arguments for the NugetPacker tool.
    /// </summary>
    /// <remarks>
    /// Almost all of the arguments here are a copy of the arguments that are accepted by the NuGet CLI.
    /// Some options are excluded because they're not relevant for our use case.
    /// See here for CLI arguments: https://learn.microsoft.com/en-us/nuget/reference/cli-reference/cli-ref-pack
    /// Additionally, this class picks up some additional arguments that are not part of the NuGet CLI
    /// that are utilized interally by the NuGet client libraries:
    /// https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Commands/CommandArgs/PackArgs.cs
    /// </remarks>
    internal sealed partial class NugetPackerArgs : CommandLineUtilities
    {
        /// <summary>
        /// Collection of additional properties for the package being packed.
        /// </summary>
        private readonly Dictionary<string, string> m_properties = [];

        /// <summary>
        /// Absolute path to nuspec file for package being packed.
        /// </summary>
        public string NuSpecPath { get; }

        /// <summary>
        /// Prevents inclusion of empty directories when building the package.
        /// </summary>
        public bool ExcludeEmptyDirectories { get; } = false;

        /// <summary>
        /// Prevents default exclusion of NuGet package files and files and folders starting with a dot, such as .svn and .gitignore.
        /// </summary>
        public bool NoDefaultExcludes { get; } = false;

        /// <summary>
        /// Specifies that pack should not run package analysis after building the package.
        /// </summary>
        public bool NoPackageAnalysis { get; } = false;

        /// <summary>
        /// Specifies the folder in which the created package is stored. If no folder is specified, the current folder is used.
        /// </summary>
        public string OutputDirectory { get; }

        /// <summary>
        /// Set the minClientVersion attribute for the created package. This value will override the value of the existing minClientVersion attribute (if any) in the .nuspec file.
        /// </summary>
        public Version MinClientVersion { get; }

        /// <summary>
        /// Specifies the amount of detail displayed in the output: normal (the default), quiet, or detailed.
        /// </summary>
        public NugetConsoleLogger.Verbosity Verbosity { get; } = NugetConsoleLogger.Verbosity.Normal;

        /// <summary>
        /// When parsing the nuspec manifest, the parser may call this delegate to
        /// get values of properties that are overriden by command line arguments.
        /// </summary>
        [JsonIgnore]
        public Func<string, string> GetProperty => (string propertyName) =>
        {
            if (m_properties.TryGetValue(propertyName, out string value))
            {
                return value;
            }

            return null;
        };

        /// <nodoc />
        public NugetPackerArgs(IReadOnlyCollection<string> args) : base(args)
        {
            foreach (var opt in Options)
            {
                if (opt.Name.Equals(nameof(NuSpecPath), StringComparison.OrdinalIgnoreCase))
                {
                    NuSpecPath = opt.Value;
                }
                else if (opt.Name.Equals(nameof(NoDefaultExcludes), StringComparison.OrdinalIgnoreCase))
                {
                    NoDefaultExcludes = true;
                }
                else if (opt.Name.Equals(nameof(OutputDirectory), StringComparison.OrdinalIgnoreCase))
                {
                    OutputDirectory = opt.Value;
                }
                else if (opt.Name.Equals(nameof(Verbosity), StringComparison.OrdinalIgnoreCase))
                {
                    Verbosity = opt.Value.ToLowerInvariant() switch
                    {
                        "quiet" => NugetConsoleLogger.Verbosity.Quiet,
                        "normal" => NugetConsoleLogger.Verbosity.Normal,
                        "detailed" => NugetConsoleLogger.Verbosity.Detailed,
                        _ => throw Error($"Invalid verbosity level '{opt.Value}'."),
                    };
                }
                else if (opt.Name.Equals(nameof(ExcludeEmptyDirectories), StringComparison.OrdinalIgnoreCase))
                {
                    ExcludeEmptyDirectories = true;
                }
                else if (opt.Name.Equals(nameof(NoPackageAnalysis), StringComparison.OrdinalIgnoreCase))
                {
                    NoPackageAnalysis = true;
                }
                else if (opt.Name.Equals(nameof(MinClientVersion), StringComparison.OrdinalIgnoreCase))
                {
                    if (!Version.TryParse(opt.Value, out Version version))
                    {
                        throw Error($"Invalid version '{opt.Value}' for MinClientVersion.");
                    }

                    MinClientVersion = version;
                }
            }

            VerifyArguments();
        }

        /// <summary>
        /// Validates required arguments.
        /// </summary>
        private void VerifyArguments()
        {
            if (string.IsNullOrEmpty(NuSpecPath))
            {
                MissingRequiredOption("NuSpecPath");
            }
            if (string.IsNullOrEmpty(OutputDirectory))
            {
                MissingRequiredOption("OutputDirectory");
            }

            if (!File.Exists(NuSpecPath))
            {
                throw Error($"NuSpec path '{NuSpecPath}' does not exist.");
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}