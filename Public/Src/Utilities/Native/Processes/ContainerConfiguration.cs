// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Native.Processes.Windows;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

#nullable enable

namespace BuildXL.Processes.Containers
{
    /// <summary>
    /// Contains the information to configure a container for a given process pip
    /// </summary>
    /// <remarks>
    /// The relationship between original and redirected directories are many-to-many. The collection of original
    /// directories virtualized to a given redirected directory are layered in order. This means that if two original
    /// directories in this collection contain the same filename, the first one will 'win' over the later one.
    /// </remarks>
    public sealed class ContainerConfiguration
    {
        /// <nodoc/>
        public bool IsIsolationEnabled { get; }

        /// <summary>
        /// Map from redirected directories to collection of original directories to be virtualized from.
        /// </summary>
        /// <remarks>
        /// Each value in this map points to the upmost de-duplicated (so directories are never pairwise nested) original directory
        /// that is either an opaque directory or the containing directory of a declared file.
        /// </remarks>
        public IReadOnlyDictionary<ExpandedAbsolutePath, IReadOnlyList<ExpandedAbsolutePath>> RedirectedDirectories { get; }

        /// <summary>
        /// Map from original directories to the redirected directories that are virtualized to
        /// </summary>
        /// <remarks>
        /// Each key on this map is either an opaque directory root or the containing directory of a declared file
        /// </remarks>
        public IReadOnlyDictionary<AbsolutePath, IReadOnlyList<ExpandedAbsolutePath>> OriginalDirectories { get; }

        /// <summary>
        /// On Windows, defines whether the WCI (Windows Container) filter driver is enabled. When false,
        /// only BindFlt is used for output redirection.
        /// </summary>
        public bool EnableWciFilter { get; }

        /// <summary>
        /// Paths that should not have BindFlt output path transformations applied to them.
        /// </summary>
        public IReadOnlySet<ExpandedAbsolutePath>? BindFltExcludedPaths { get; }

        /// <summary>
        /// Additional flags to pass for BindFlt filter configuration.
        /// </summary>
        public NativeContainerUtilities.BfSetupFilterFlags BindFltFlags { get; }

        /// <summary>
        /// Optional callback to allow custom container configuration of a job object.
        /// When non-null, the WCI and Bind filters are enabled for the job object, but no
        /// container layer or BindFlt path mappings are applied.
        /// First parameter is the Windows job object handle. Second parameter is a
        /// non-threadsafe collection of non-fatal warnings to be passed back to the caller
        /// for logging and debugging purposes.
        /// </summary>
        public Action<IntPtr, ICollection<string>>? CustomJobObjectCustomization { get; }

        /// <summary>
        /// No isolation
        /// </summary>
        public static ContainerConfiguration DisabledIsolation = new ContainerConfiguration(
                pathTable: null,
                redirectedDirectories: CollectionUtilities.EmptyDictionary<ExpandedAbsolutePath, IReadOnlyList<ExpandedAbsolutePath>>(),
                originalDirectories: CollectionUtilities.EmptyDictionary<AbsolutePath, IReadOnlyList<ExpandedAbsolutePath>>());

        /// <summary>
        /// Creates a configuration created for a specific process
        /// </summary>
        public ContainerConfiguration(
            PathTable? pathTable,
            IReadOnlyDictionary<ExpandedAbsolutePath, IReadOnlyList<ExpandedAbsolutePath>> redirectedDirectories,
            IReadOnlyDictionary<AbsolutePath, IReadOnlyList<ExpandedAbsolutePath>> originalDirectories,
            bool enableWciFilter = true,
            IReadOnlySet<ExpandedAbsolutePath>? bindFltExcludedPaths = null,
            NativeContainerUtilities.BfSetupFilterFlags bindFltFlags = NativeContainerUtilities.BfSetupFilterFlags.None,
            Action<IntPtr, ICollection<string>>? customJobObjectCustomization = null)
        {
            Contract.Requires(redirectedDirectories.Count == 0 || pathTable != null || customJobObjectCustomization is not null);

            IsIsolationEnabled = redirectedDirectories.Count > 0 || customJobObjectCustomization is not null;
            RedirectedDirectories = redirectedDirectories;
            OriginalDirectories = originalDirectories;
            EnableWciFilter = enableWciFilter;
            BindFltExcludedPaths = bindFltExcludedPaths ?? CollectionUtilities.EmptySet<ExpandedAbsolutePath>();
            BindFltFlags = bindFltFlags;
            CustomJobObjectCustomization = customJobObjectCustomization;
        }

        /// <summary>
        /// Creates a container configuration info for testing purposes, where the remapping for directories can be passed explicitly as a collection of (originalDirectory, redirectedDirectory)
        /// </summary>
        public static ContainerConfiguration CreateConfigurationForTesting(PathTable pathTable, IEnumerable<(string originalDirectory, string redirectedDirectory)> directoryRemapping)
        {
            var remapping = directoryRemapping.Select(tuple =>
                (
                    originalDirectory: new ExpandedAbsolutePath(AbsolutePath.Create(pathTable, tuple.originalDirectory), pathTable),
                    redirectedDirectory: new ExpandedAbsolutePath(AbsolutePath.Create(pathTable, tuple.redirectedDirectory), pathTable)
                )
            ).ToList();

            var originalDirectories = new MultiValueDictionary<AbsolutePath, ExpandedAbsolutePath>();
            var redirectedDirectories = new MultiValueDictionary<ExpandedAbsolutePath, ExpandedAbsolutePath>();

            foreach (var kvp in remapping)
            {
                originalDirectories.Add(kvp.originalDirectory.Path, kvp.redirectedDirectory);
                redirectedDirectories.Add(kvp.redirectedDirectory, kvp.originalDirectory);
            }

            return new ContainerConfiguration(
                pathTable: pathTable,
                redirectedDirectories,
                originalDirectories);
        }

        /// <summary>
        /// User-facing representation of the container configuration
        /// </summary>
        public string ToDisplayString()
        {
            var sb = new StringBuilder(256);
            sb.AppendLine($"Isolation Enabled: {IsIsolationEnabled}");
            sb.AppendLine($"WCI Filter Enabled: {EnableWciFilter}");
            if (RedirectedDirectories.Count > 0)
            {
                sb.AppendLine("Remapped directories:");
                foreach (var kvp in RedirectedDirectories)
                {
                    sb.AppendLine(I($"[{string.Join(Environment.NewLine, kvp.Value)}]' -> '{kvp.Key}'"));
                }
            }

            return sb.ToString();
        }
    }
}
