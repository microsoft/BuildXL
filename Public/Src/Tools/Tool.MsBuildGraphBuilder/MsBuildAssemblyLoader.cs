// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Build.Tasks;

namespace MsBuildGraphBuilderTool
{
    /// <summary>
    /// Loads all required MsBuild assemblies needed for constructing a static graph and finds MsBuild.exe
    /// </summary>
    public sealed class MsBuildAssemblyLoader : IMsBuildAssemblyLoader
    {
        private const string MSBuildPublicKeyToken = "b03f5f7f11d50a3a";
        private const string DotNetPublicToken = "cc7b13ffcd2ddd51";

        private static readonly Version s_minimumRequiredMsBuildAssemblyVersion = new Version("15.1.0.0");
        private static readonly Version s_minimumRequiredMsBuildFileVersion = new Version("16.4.0.51602");

        // List of required assemblies
        private const string MicrosoftBuild = "Microsoft.Build.dll";
        private const string MicrosoftBuildFramework = "Microsoft.Build.Framework.dll";
        private const string MicrosoftBuildUtilities = "Microsoft.Build.Utilities.Core.dll";
        private const string SystemCollectionsImmutable = "System.Collections.Immutable.dll";
        private const string SystemThreadingDataflow = "System.Threading.Tasks.Dataflow.dll";

        // MsBuild is not a required to be loaded but is required to be found
        // Under DotNetCore there is not an msbuild.exe but a msbuild.dll
        private string m_msbuild;

        private readonly string[] m_assemblyNamesToLoad;

        /// <summary>
        /// The expected public token for each required assembly
        /// </summary>
        private readonly Dictionary<string, string> m_assemblyPublicTokens;

        private static ResolveEventHandler s_msBuildHandler;

        // Assembly resolution is multi-threaded
        private readonly ConcurrentDictionary<string, Assembly> m_loadedAssemblies;

        public MsBuildAssemblyLoader(bool msBuildRuntimeIsDotNetCore)
        {
            m_msbuild = msBuildRuntimeIsDotNetCore ? "MSBuild.dll" : "MSBuild.exe";

            m_assemblyNamesToLoad = new[]
            {
                m_msbuild,
                MicrosoftBuild,
                MicrosoftBuildFramework,
                MicrosoftBuildUtilities,
                SystemCollectionsImmutable,
                SystemThreadingDataflow
            };

            m_assemblyPublicTokens = new Dictionary<string, string>
            {
                [m_msbuild] = MSBuildPublicKeyToken,
                [MicrosoftBuild] = MSBuildPublicKeyToken,
                [MicrosoftBuildFramework] = MSBuildPublicKeyToken,
                [MicrosoftBuildUtilities] = MSBuildPublicKeyToken,
                [SystemCollectionsImmutable] = DotNetPublicToken,
                [SystemThreadingDataflow] = DotNetPublicToken,
            };

            m_loadedAssemblies = new ConcurrentDictionary<string, Assembly>(5, m_assemblyNamesToLoad.Length, StringComparer.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public bool TryLoadMsBuildAssemblies(
            IEnumerable<string> searchLocations, 
            GraphBuilderReporter reporter, 
            out string failureReason, 
            out IReadOnlyDictionary<string, string> locatedAssemblyPaths, 
            out string locatedMsBuildExePath)
        {
            // We want to make sure the provided locations actually contain all the needed assemblies
            if (!TryGetAssemblyLocations(searchLocations, reporter, out locatedAssemblyPaths, out IReadOnlyDictionary<string, string> missingAssemblyNames, out locatedMsBuildExePath))
            {
                var locationString = string.Join(", ", searchLocations.Select(location => location));
                var missingAssemblyString = string.Join(", ", missingAssemblyNames.Select(nameAndReason => $"['{nameAndReason.Key}' Reason: {nameAndReason.Value}]"));

                failureReason = $"Cannot find the required MSBuild toolset. Missing assemblies: {missingAssemblyString}. Searched locations: [{locationString}]";
                return false;
            }

            var assemblyPathsToLoad = locatedAssemblyPaths;
            s_msBuildHandler = (_, eventArgs) =>
            {
                var assemblyNameString = eventArgs.Name;

                var assemblyName = new AssemblyName(assemblyNameString).Name + ".dll";

                // If we already loaded the requested assembly, just return it
                if (m_loadedAssemblies.TryGetValue(assemblyName, out var assembly))
                {
                    return assembly;
                }

                if (assemblyPathsToLoad.TryGetValue(assemblyName, out string targetAssemblyPath))
                {
                    assembly = Assembly.LoadFrom(targetAssemblyPath);
                    m_loadedAssemblies.TryAdd(assemblyNameString, assembly);

                    // Automatically un-register the handler once all supported assemblies have been loaded.
                    // No need to synchronize threads here, if the handler was already removed, nothing bad happens
                    if (m_loadedAssemblies.Count == m_assemblyNamesToLoad.Length)
                    {
                        AppDomain.CurrentDomain.AssemblyResolve -= s_msBuildHandler;
                    }

                    return assembly;
                }

                return null;
            };

            AppDomain.CurrentDomain.AssemblyResolve += s_msBuildHandler;

            failureReason = string.Empty;
            return true;
        }

        private bool TryGetAssemblyLocations(
            IEnumerable<string> locations,
            GraphBuilderReporter reporter,
            out IReadOnlyDictionary</* assembly name */ string, /* full path */ string> assemblyPathsToLoad, 
            out IReadOnlyDictionary</* assembly name */ string, /* not found reason */ string> missingAssemblies, 
            out string locatedMsBuildExePath)
        {
            var foundAssemblies = new Dictionary<string, string>(m_assemblyNamesToLoad.Length, StringComparer.OrdinalIgnoreCase);
            var notFoundAssemblies = new Dictionary<string, string>(m_assemblyNamesToLoad.Length, StringComparer.OrdinalIgnoreCase);

            var assembliesToFind = new HashSet<string>(m_assemblyNamesToLoad, StringComparer.OrdinalIgnoreCase);
            locatedMsBuildExePath = string.Empty;

            foreach (var location in locations)
            {
                reporter.ReportMessage($"Looking for MSBuild toolset in '{location}'.");

                Contract.Assert(!string.IsNullOrEmpty(location), "Specified search location must not be null or empty");
                try
                {
                    var dlls = Directory.EnumerateFiles(location, "*.dll", SearchOption.TopDirectoryOnly);
                    var msBuildExe = Directory.EnumerateFiles(location, m_msbuild, SearchOption.TopDirectoryOnly);

                    foreach (string fullPath in dlls.Union(msBuildExe))
                    {
                        var file = Path.GetFileName(fullPath);
                        if (assembliesToFind.Contains(Path.GetFileName(fullPath)))
                        {
                            var assemblyName = AssemblyName.GetAssemblyName(fullPath);

                            if (IsMsBuildAssembly(assemblyName, fullPath, out string notFoundReason))
                            {
                                assembliesToFind.Remove(file);
                                notFoundAssemblies.Remove(file);

                                // If the file found is msbuild.exe, we update the associated location but don't store the path
                                // in foundAssemblies, since this is not an assembly we really want to load
                                if (file.Equals(m_msbuild, StringComparison.OrdinalIgnoreCase))
                                {
                                    locatedMsBuildExePath = fullPath;
                                }
                                else
                                {
                                    // We store the first occurrence
                                    if (!foundAssemblies.ContainsKey(file))
                                    {
                                        foundAssemblies[file] = fullPath;
                                    }
                                }

                                // Shortcut the search if we already found all required ones
                                if (assembliesToFind.Count == 0)
                                {
                                    break;
                                }
                            }
                            else
                            {
                                // We want to store the first reason for not being able to find an assembly that actually looks like the right one
                                if (!notFoundAssemblies.ContainsKey(file))
                                {
                                    notFoundAssemblies.Add(file, notFoundReason);
                                }
                            }

                        }
                    }

                    // Shortcut the search if we already found all required ones
                    if (assembliesToFind.Count == 0)
                    {
                        break;
                    }
                }
                // Ignore unauthorized and IO exceptions: if we cannot get to the specified locations, we should skip those
                catch (UnauthorizedAccessException ex)
                {
                    reporter.ReportMessage($"Search location '{location}' is skipped because the process is not authorized to enumerate it. Details: {ex.Message}");
                }
                catch (IOException ex)
                {
                    reporter.ReportMessage($"Search location '{location}' is skipped because an IO exception occurred while enumerating it. Details: {ex.Message}");
                }
            }

            // When we are done enumerating the locations to search, if there are still assemblies we haven't found,
            // add them to the not found set with the right reason
            foreach (var assemblyName in assembliesToFind)
            {
                if (!notFoundAssemblies.ContainsKey(assemblyName))
                {
                    notFoundAssemblies.Add(assemblyName, "Assembly not found.");
                }
            }

            missingAssemblies = notFoundAssemblies;
            assemblyPathsToLoad = foundAssemblies;

            return assembliesToFind.Count == 0;
        }

        private bool IsMsBuildAssembly(AssemblyName assemblyName, string fullPathToAssembly, out string notFoundReason)
        {
            if (string.Equals($"{assemblyName.Name}.dll", MicrosoftBuild, StringComparison.OrdinalIgnoreCase))
            {
                // Check the assembly version. The minimum version guarantees that the static graph APIs are available
                if (assemblyName.Version < s_minimumRequiredMsBuildAssemblyVersion)
                {
                    notFoundReason = $"The minimum required assembly version is {s_minimumRequiredMsBuildAssemblyVersion} but the assembly under '{fullPathToAssembly}' has version {assemblyName.Version}.";
                    return false;
                }

                // Check the file version. For the case of MSBuild assemblies this should always be present. This guarantees
                // the right set of APIs are available, since they are still experimental and breaking changes have been made
                var versionInfo = FileVersionInfo.GetVersionInfo(fullPathToAssembly);
                Version fileVersion = null;
                if (versionInfo == null || !Version.TryParse(versionInfo.FileVersion, out fileVersion) || fileVersion < s_minimumRequiredMsBuildFileVersion)
                {
                    notFoundReason = $"The minimum required file version is {s_minimumRequiredMsBuildFileVersion} but the assembly under '{fullPathToAssembly}' has file version {(fileVersion != null? fileVersion.ToString() : "[not available]")}.";
                    return false;
                }
            }

            var publicKeyToken = assemblyName.GetPublicKeyToken();

            if (publicKeyToken == null || publicKeyToken.Length == 0)
            {
                notFoundReason = $"The assembly under '{fullPathToAssembly}' does not contain a public token.";
                return false;
            }

            var sb = new StringBuilder();
            foreach (var b in publicKeyToken)
            {
                sb.Append($"{b:x2}");
            }

            if (m_assemblyPublicTokens.TryGetValue(assemblyName.Name, out string publicToken) && !sb.ToString().Equals(publicToken, StringComparison.OrdinalIgnoreCase))
            {
                notFoundReason = $"The assembly under '{fullPathToAssembly}' is expected to contain public token '{publicToken}' but '{sb}' was found.";
                return false;
            }

            notFoundReason = string.Empty;
            return true;
        }
    }
}
