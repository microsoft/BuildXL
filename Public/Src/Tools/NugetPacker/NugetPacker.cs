// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Rules;
using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;

namespace Tool.Nuget.Packer
{
    /// <summary>
    /// Packs a nuget package when provided with a NuSpec file.
    /// </summary>
    /// <remarks>
    /// Code in this file is a slimmer version of the NuGet CLI's pack command.
    /// See:
    /// https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Clients/NuGet.CommandLine/Commands/PackCommand.cs
    /// https://github.com/NuGet/NuGet.Client/blob/8791d42fb1e7582f9a0b92d1708133c3b138732a/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs#L657
    /// </remarks>
    internal sealed class NugetPacker : ToolProgram<NugetPackerArgs>
    {
        private readonly NugetConsoleLogger m_logger = new();

        public static int Main(string[] args)
        {
            return new NugetPacker().MainHandler(args);
        }

        private NugetPacker() : base("NugetPacker") { }

        /// <inheritdoc />
        public override int Run(NugetPackerArgs arguments)
        {
            try
            {
                var builder = new PackageBuilder(
                    arguments.NuSpecPath,
                    arguments.GetProperty,
                    includeEmptyDirectories: !arguments.ExcludeEmptyDirectories,
                     // NOTE: this value is already hardcoded to false in the Nuget libraries due to a bug.
                     //       No matter what value we set here it will be ignored.
                    deterministic: false,
                    logger: m_logger);

                var outputPath = GetOutputPath(builder, arguments);

                InitCommonPackageBuilderProperties(builder, arguments);

                BuildPackage(builder, outputPath, arguments);
            }
            catch (Exception ex)
            {
                m_logger.LogError($"Nuget Packer Arguments: '{arguments}'");
                m_logger.LogError(ex.ToString());

                return 1;
            }

            return 0;
        }

        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out NugetPackerArgs arguments)
        {
            try
            {
                arguments = new NugetPackerArgs(rawArgs);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetLogEventMessage());
                arguments = null;
                return false;
            }
        }

        /// <summary>
        /// Gets the output path for the package including the filename of the package.
        /// </summary>
        private static string GetOutputPath(PackageBuilder builder, NugetPackerArgs arguments)
        {
            Contract.Requires(builder.Version != null, "Version is required in the nuspec file.");

            return Path.Combine(arguments.OutputDirectory, GetOutputFileName(builder));
        }

        /// <summary>
        /// Gets the filename of the package in the format `<packageName>.<versionNumver>.nupkg`.
        /// </summary>
        private static string GetOutputFileName(PackageBuilder builder)
        {
            Contract.Requires(builder.Version != null, "Version is required in the nuspec file.");

            var normalizedVersion = builder.Version.ToNormalizedString();
            var outputFile = builder.Id + "." + normalizedVersion + NuGetConstants.PackageExtension;

            return outputFile;
        }

        private void InitCommonPackageBuilderProperties(PackageBuilder builder, NugetPackerArgs arguments)
        {
            if (arguments.MinClientVersion != null)
            {
                builder.MinClientVersion = arguments.MinClientVersion;
            }

            CheckForUnsupportedFrameworks(builder);
            ExcludeFiles(builder.Files, arguments);
        }

        /** 
         * NOTE: The utility functions below were copy pasted from the NuGet CLI source code to ensure that our implementation matches theirs.
         * Some functions contain modifications to remove unused arguments.
         * Reference: https://github.com/NuGet/NuGet.Client/blob/8791d42fb1e7582f9a0b92d1708133c3b138732a/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs
         **/

        /// <summary>
        /// Validates if any of the referenced frameworks in the package are unsupported.
        /// </summary>
        /// <remarks>
        /// Reference: https://github.com/NuGet/NuGet.Client/blob/45a6a09e4dc08909a0c287da9e7f0a2c08d77f54/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs#L852
        /// </remarks>
        private void CheckForUnsupportedFrameworks(PackageBuilder builder)
        {
            foreach (FrameworkAssemblyReference reference in builder.FrameworkReferences)
            {
                foreach (NuGetFramework framework in reference.SupportedFrameworks)
                {
                    if (framework.IsUnsupported)
                    {
                        throw new Exception($"Failed to build package because of an unsupported targetFramework value on '{reference.AssemblyName}'.");
                    }
                }
            }
        }

        /// <summary>
        /// Excludes files not needed by the package
        /// </summary>
        /// <remarks>
        /// Reference: https://github.com/NuGet/NuGet.Client/blob/45a6a09e4dc08909a0c287da9e7f0a2c08d77f54/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs#L909
        /// </remarks>
        private void ExcludeFiles(ICollection<IPackageFile> files, NugetPackerArgs arguments)
        {
            var excludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Always exclude the nuspec file
            // Review: This exclusion should be done by the package builder because it knows which file would collide with the auto-generated
            // manifest file.
            IEnumerable<string> wildCards = excludes.Concat(new[] { "**" + NuGetConstants.ManifestExtension });

            if (!arguments.NoDefaultExcludes)
            {
                // The user has not explicitly disabled default filtering.
                IEnumerable<IPackageFile> excludedFiles = RemoveDefaultExclusions(files);
                if (excludedFiles != null)
                {
                    foreach (IPackageFile file in excludedFiles)
                    {
                        if (file is PhysicalPackageFile)
                        {
                            var physicalPackageFile = file as PhysicalPackageFile;
                            m_logger.LogWarning($"File '{physicalPackageFile.SourcePath}' was not added to the package. Files and folders starting with '.' or ending with '.nupkg' are excluded by default. To include this file, use -NoDefaultExcludes from the commandline");
                        }
                    }
                }
            }

            PathResolver.FilterPackageFiles(files, ResolvePath, wildCards);
        }

        /// <summary>
        /// Removes default exclusions
        /// </summary>
        /// <remarks>
        /// Reference: https://github.com/NuGet/NuGet.Client/blob/45a6a09e4dc08909a0c287da9e7f0a2c08d77f54/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs#L944
        /// </remarks>
        private IEnumerable<IPackageFile> RemoveDefaultExclusions(ICollection<IPackageFile> packageFiles)
        {
            string basePath = Directory.GetCurrentDirectory();

            var matches = packageFiles.Where(packageFile =>
            {
                var filePath = ResolvePath(packageFile, basePath);
                var fileName = Path.GetFileName(filePath);

                return fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                    || (fileName.StartsWith(".", StringComparison.Ordinal) && fileName.IndexOf(".", startIndex: 1, StringComparison.Ordinal) == -1);
            });

            var matchedFiles = new HashSet<IPackageFile>(matches);
            var toRemove = packageFiles.Where(matchedFiles.Contains).ToList();

            foreach (var item in toRemove)
            {
                packageFiles.Remove(item);
            }

            return toRemove;
        }

        /// <summary>
        /// Resolve path against working directory.
        /// </summary>
        /// <remarks>
        /// Reference: https://github.com/NuGet/NuGet.Client/blob/45a6a09e4dc08909a0c287da9e7f0a2c08d77f54/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs#L968
        /// </remarks>
        private string ResolvePath(IPackageFile packageFile)
        {
            string basePath = Directory.GetCurrentDirectory();

            return ResolvePath(packageFile, basePath);
        }

        /// <summary>
        /// Resolve path.
        /// </summary>
        /// <remarks>
        /// Reference: https://github.com/NuGet/NuGet.Client/blob/45a6a09e4dc08909a0c287da9e7f0a2c08d77f54/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs#L976
        /// </remarks>
        private static string ResolvePath(IPackageFile packageFile, string basePath)
        {
            var physicalPackageFile = packageFile as PhysicalPackageFile;

            // For PhysicalPackageFiles, we want to filter by SourcePaths, the path on disk. The Path value maps to the TargetPath
            if (physicalPackageFile == null)
            {
                return packageFile.Path;
            }

            string path = physicalPackageFile.SourcePath;

            // Make sure that the basepath has a directory separator
            int index = path.IndexOf(PathUtility.EnsureTrailingSlash(basePath), StringComparison.OrdinalIgnoreCase);
            if (index != -1)
            {
                // Since wildcards are going to be relative to the base path, remove the BasePath portion of the file's source path.
                // Also remove any leading path separator slashes
                path = path.Substring(index + basePath.Length).TrimStart(Path.DirectorySeparatorChar);
            }

            return path;
        }

        /// <summary>
        /// Writes nupkg file to disk.
        /// </summary>
        private void BuildPackage(PackageBuilder builder, string outputPath, NugetPackerArgs arguments)
        {
            // If the file is already on disk, delete it
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            // Create parent directory if it doesn't already exist
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            try
            {
                using var stream = File.Create(outputPath);
                builder.Save(stream);
            }
            catch
            {
                m_logger.LogError($"Failed to create package at {outputPath}");
                throw;
            }


            if (!arguments.NoPackageAnalysis)
            {
                using var package = new PackageArchiveReader(outputPath);
                AnalyzePackage(package);
            }

            m_logger.LogInformation($"Successfully created package at {outputPath}");

            if (arguments.Verbosity == NugetConsoleLogger.Verbosity.Detailed)
            {
                PrintVerbose(builder, outputPath);
            }
        }

        /// <summary>
        /// Analye the generated nuget package.
        /// </summary>
        /// <remarks>
        /// Reference: https://github.com/NuGet/NuGet.Client/blob/45a6a09e4dc08909a0c287da9e7f0a2c08d77f54/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs#L1031
        /// </remarks>
        private void AnalyzePackage(PackageArchiveReader package)
        {
            if (package == null)
            {
                // This seems to be best effort on the nuget cli code, we don't fail here.
                return;
            }

            var logMessages = new List<PackagingLogMessage>();

            foreach (var rule in RuleSet.PackageCreationRuleSet)
            {
                logMessages.AddRange(rule.Validate(package).OrderBy(p => p.Code.ToString(), StringComparer.CurrentCulture));
            }

            if (logMessages.Count > 0)
            {
                foreach (PackagingLogMessage logMessage in logMessages)
                {
                    if (!string.IsNullOrEmpty(logMessage.Message))
                    {
                        m_logger.LogWarning(logMessage.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Print debug string.
        /// </summary>
        /// <remarks>
        /// Reference: https://github.com/NuGet/NuGet.Client/blob/45a6a09e4dc08909a0c287da9e7f0a2c08d77f54/src/NuGet.Core/NuGet.Commands/CommandRunners/PackCommandRunner.cs#L868
        /// </remarks>
        private void PrintVerbose(PackageBuilder builder, string outputPath)
        {
            m_logger.LogInformation(string.Empty);

            using var package = new PackageArchiveReader(outputPath);

            m_logger.LogInformation($"Id: {builder.Id}");
            m_logger.LogInformation($"Version: {builder.Version}");
            m_logger.LogInformation($"Authors: {string.Join(", ", builder.Authors)}");
            m_logger.LogInformation($"Description: {builder.Description}");
            if (builder.LicenseUrl != null)
            {
                m_logger.LogInformation($"License Url: {builder.LicenseUrl}");
            }
            if (builder.ProjectUrl != null)
            {
                m_logger.LogInformation($"Project Url: {builder.ProjectUrl}");
            }
            if (builder.Tags.Any())
            {
                m_logger.LogInformation($"Tags: {string.Join(", ", builder.Tags)}");
            }
            if (builder.DependencyGroups.Count > 0)
            {
                m_logger.LogInformation($"Dependencies: {string.Join(", ", builder.DependencyGroups.SelectMany(d => d.Packages).Select(d => d.ToString()))}");
            }
            else
            {
                m_logger.LogInformation($"Dependencies: None");
            }

            m_logger.LogInformation(string.Empty);

            foreach (string file in package.GetFiles().OrderBy(p => p))
            {
                m_logger.LogInformation($"Added File {file}");
            }

            m_logger.LogInformation(string.Empty);
        }
    }
}