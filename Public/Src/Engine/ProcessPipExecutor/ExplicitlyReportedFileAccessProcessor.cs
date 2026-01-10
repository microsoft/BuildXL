// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// This class as a helper for <see cref="SandboxedProcessPipExecutor.TryGetObservedFileAccesses"/> so explicitly reported file accesses are processes as they are reported.
    /// </summary>
    /// <remarks>
    /// The main purpose of this class is to avoid having to wait until all accesses are reported before starting processing them. For pips with a high number of reported accesses, waiting till the end
    /// adds to the critical path of the build. By processing accesses as they are reported, we can overlap processing with the pip execution time.
    /// Some processing needs to be deferred until all accesses are reported, and <see cref="SandboxedProcessPipExecutor.TryGetObservedFileAccesses"/> is still in charge of that.
    /// This component is shared to the the sandboxed process reports via <see cref="IExplicitlyReportedAccesses"/> interface.
    /// </remarks>
    public sealed class ExplicitlyReportedFileAccessProcessor : IExplicitlyReportedAccesses
    {
        private readonly PipExecutionContext m_context;
        private readonly LoggingContext m_loggingContext;
        private readonly PathTable m_pathTable;
        private readonly Process m_pip;
        private readonly ISandboxFileSystemView m_fileSystemView;
        private readonly ISandboxConfiguration m_sandboxConfig;
        private readonly bool m_shouldPreserveOutputs;
        private readonly string[] m_incrementalToolFragments;
        private readonly FileAccessManifest m_fileAccessManifest;
        private readonly IConfiguration m_configuration;
        private readonly HashSet<AbsolutePath> m_pipOutputPaths;
        private readonly SemanticPathExpander m_semanticPathExpander;
        private readonly IReadOnlyDictionary<AbsolutePath, DirectoryArtifact> m_sharedOpaqueDirectoryRoots;
        private readonly ISet<AbsolutePath> m_directorySymlinksAsDirectories;
        private readonly FileAccessReportingContext m_fileaccessReportingContext;
        private readonly HashSet<AbsolutePath> m_allInputPathsUnderSharedOpaques;
        private readonly IPipGraphFileSystemView m_pipGraphFileSystemView;

        // Mutable state
        private bool m_isFrozen = false;
        private readonly ExplicitlyReportedFileAccessProcessorResult m_result;
        private readonly HashSet<ReportedFileAccess> m_explicitlyReportedFileAccesses = new HashSet<ReportedFileAccess>();

        // Act as processing cached state
        private readonly HashSet<(AbsolutePath, AbsolutePath)> m_excludedToolsAndPaths = new HashSet<(AbsolutePath, AbsolutePath)>();
        private readonly ObjectCache<string, bool> m_incrementalToolMatchCache = new(37);

        private bool IsIncrementalPreserveOutputPip => m_shouldPreserveOutputs && m_pip.IncrementalTool;

        private static bool EnableFullReparsePointResolving(IConfiguration configuration, Process process) =>
            configuration.EnableFullReparsePointResolving() && !process.DisableFullReparsePointResolving;

        /// <inheritdoc/>
        public ISet<ReportedFileAccess> ExplicitlyReportedFileAccesses() => m_explicitlyReportedFileAccesses;

        /// <nodoc/>
        public ExplicitlyReportedFileAccessProcessor(
            IConfiguration configuration,
            bool shouldPreserveOutputs,
            FileAccessManifest fileAccessManifest,
            SemanticPathExpander semanticPathExpander,
            ISet<AbsolutePath> directorySymlinksAsDirectories,
            FileAccessReportingContext fileAccessReportingContext,
            HashSet<AbsolutePath> allInputPathsUnderSharedOpaques,
            IPipGraphFileSystemView pipGraphFileSystemView,
            ISandboxFileSystemView sandboxFileSystemView = null)
        {
            m_context = fileAccessReportingContext.PipExecutionContextContext;
            m_loggingContext = fileAccessReportingContext.LoggingContext;
            m_pathTable = m_context.PathTable;
            m_pip = fileAccessReportingContext.Pip;
            m_fileSystemView = sandboxFileSystemView;
            m_configuration = configuration;
            m_sandboxConfig = configuration.Sandbox;
            m_shouldPreserveOutputs = shouldPreserveOutputs;
            m_fileAccessManifest = fileAccessManifest;
            m_directorySymlinksAsDirectories = directorySymlinksAsDirectories;
            m_fileaccessReportingContext = fileAccessReportingContext;
            m_allInputPathsUnderSharedOpaques = allInputPathsUnderSharedOpaques;
            m_pipGraphFileSystemView = pipGraphFileSystemView;
            m_result = new ExplicitlyReportedFileAccessProcessorResult(m_pathTable);
            
            if (configuration.IncrementalTools != null)
            {
                m_incrementalToolFragments = configuration.IncrementalTools.Select(toolSuffix =>
                    // Append leading separator to ensure suffix only matches valid relative path fragments
                    Path.DirectorySeparatorChar + toolSuffix.ToString(m_context.StringTable)).ToArray();
            }
            else
            {
                m_incrementalToolFragments = Array.Empty<string>();
            }

            m_pipOutputPaths = m_pip.FileOutputs.Select(f => f.Path).ToHashSet();
            m_semanticPathExpander = semanticPathExpander;

            m_sharedOpaqueDirectoryRoots = m_pip.DirectoryOutputs
               .Where(directory => directory.IsSharedOpaque)
               .ToDictionary(directory => directory.Path, directory => directory);

            // Initializes all shared directories in the pip with no accesses
            var dynamicWriteAccesses = m_result.DynamicWriteAccesses;
            foreach (var sharedDirectory in m_sharedOpaqueDirectoryRoots.Keys)
            {
                dynamicWriteAccesses[sharedDirectory] = new HashSet<AbsolutePath>();
            }
        }

        /// <nodoc/>
        public ExplicitlyReportedFileAccessProcessorResult FreezeAndReturn()
        {
            Contract.Assert(!m_isFrozen, "ExplicitlyReportedFileAccessProcessor is already frozen");
            m_isFrozen = true;

            return m_result;
        }

        /// <inheritdoc/>
        public void Remove(ReportedFileAccess access)
        {
            Contract.Assert(!m_isFrozen, "ExplicitlyReportedFileAccessProcessor is already frozen");
            m_explicitlyReportedFileAccesses.Remove(access);

            // We are cheating here a bit because the removal case is not an arbitrary removal and we know this case is always a file existence based denied write.
            // The access is going to get added back right away as an allowed case.
            // This simplifies the logic a lot. So let's assert this is the case to secure our assumption.
            Contract.Assert(access.RequestedAccess.HasFlag(RequestedAccess.Write));
            Contract.Assert(access.Method == FileAccessStatusMethod.FileExistenceBased);

            var shouldIncludePath = ShouldIncludePath(access, out var absolutePath);
            
            // If we were not including the path in the first place, then nothing to do here.
            if (!shouldIncludePath)
            {
                return;
            }

            Contract.Assert(absolutePath.IsValid, "ShouldIncludePath should have filtered out invalid paths");
            var accessesAndFlagsByPath = m_result.AccessesByPath;
            if (!accessesAndFlagsByPath.TryGetValue(absolutePath, out ReportedFileAccessesAndFlagsMutable reportedFileAccessesAndFlags))
            {
                // This case shouldn't really happen, the access was added before so we should have an entry for it. But in any case, nothing to do here.
                return;
            }

            // The access is going to be added right away. Just remove it from the set of accesses for now. We don't need to update any of the flags since they will be recomputed when adding the allowed access.
            reportedFileAccessesAndFlags.ReportedFileAccesses = reportedFileAccessesAndFlags.ReportedFileAccesses.Remove(access);
            m_result.FileExistenceDenials.Remove(absolutePath);
        }

        /// <inheritdoc/>
        public void Add(ReportedFileAccess access)
        {
            Contract.Assert(!m_isFrozen, "ExplicitlyReportedFileAccessProcessor is already frozen");

            m_explicitlyReportedFileAccesses.Add(access);

            var shouldIncludePath = ShouldIncludePath(access, out var absolutePath);

            // Update created directories if applicable. Observe that this is orthogonal to whether we include the path in the observed accesses or not, as long as the path is valid.
            if (absolutePath.IsValid)
            {
                // We want to know if a directory was created by this pip. This means the create directory operation succeeded, but also that this directory was not deleted before by the build.
                // Directories that fall in this category were already reported in SandboxedProcessReports as report lines get received, so we only add it here if we can already find it. Consider the following cases:
                // 1) The directory was there before the build started but some other pip deletes it first. Then it will be reported in SandboxedProcessReports as a deleted directory and won't be added as a created one here. So we won't
                // consider it here as a created directory. This is correct since the directory is not actually created by the build.
                // 2) Consider now that the pip that deletes the directory in 1) is a cache hit. Removed directories are not reported on cache hit (because a cache replay does not remove them). This means the directory is now present.
                // And that means the directory cannot be effectively created by this pip (which may introduce a different behavior than the one in 1), but that's a bigger problem to solve). So we won't add it here either.
                // 3) The directory is removed and re-created by this same pip. In that case it will be reported to the output filesystem as removed and won't be added here. Similarly to 1), this is the right behavior. On cache replay, the directory
                // will never be removed, and the fact that it is still not considered as a created directory is sound.
                if (m_pip.AllowUndeclaredSourceReads
                    && access.RequestedAccess.HasFlag(RequestedAccess.Write)
                    && access.IsDirectoryEffectivelyCreated()
                    // m_fileSystemView can be null for some tests
                    && m_fileSystemView?.ExistCreatedDirectoryInOutputFileSystem(absolutePath) == true)
                {
                    m_result.CreatedDirectories.Add(absolutePath);
                }
            }

            if (!shouldIncludePath)
            {
                return;
            }

            var accessesAndFlagsByPath = m_result.AccessesByPath;

            // Now we compute the flags associated with the access. The final flags will be the union of all flags for all accesses on the same path.
            // Check if we have already seen accesses on this path, and create an entry if not.
            bool isNewPath = false;
            if (!accessesAndFlagsByPath.TryGetValue(absolutePath, out ReportedFileAccessesAndFlagsMutable reportedFileAccessesAndFlags))
            {
                reportedFileAccessesAndFlags = new ReportedFileAccessesAndFlagsMutable() {
                    ReportedFileAccesses = new CompactSet<ReportedFileAccess>(),
                    ObservationFlags = ObservationFlags.FileProbe, // Probe until proven otherwise
                    HasDirectoryReparsePointTreatedAsFile = false}; 
                accessesAndFlagsByPath[absolutePath] = reportedFileAccessesAndFlags;
                isNewPath = true;
            }
            // If an access on this path was already identified as a shared opaque output, then we can skip further processing
            else if (reportedFileAccessesAndFlags.IsSharedOpaqueOutput)
            {
                return;
            }

            // If the path is a static output of the pip, we don't need to track any further information about it and we don't want to store it as an observation
            // We leave an entry in the accessesByPath map so that we know the path was accessed, but the entry will be removed after all accesses are processed
            if (m_pipOutputPaths.Contains(absolutePath))
            {
                return;
            }

            // Unfold the current flags for ease of manipulation.
            var currentFlags = reportedFileAccessesAndFlags.ObservationFlags;
            bool isDirectoryLocation = (currentFlags & ObservationFlags.DirectoryLocation) != 0;
            bool hasEnumeration = (currentFlags & ObservationFlags.Enumeration) != 0;
            bool isProbe = (currentFlags & ObservationFlags.FileProbe) != 0;

            // If isDirectoryLocation was not already set, try one of the methods below
            bool isAccessingDirectoryLocation = IsAccessingDirectoryLocation(absolutePath, access);
            isDirectoryLocation |= isAccessingDirectoryLocation;

            reportedFileAccessesAndFlags.HasDirectoryReparsePointTreatedAsFile |=
                OperatingSystemHelper.IsWindowsOS // Only relevant on Windows.
                && !isAccessingDirectoryLocation
                && access.OpenedFileOrDirectoryAttributes.HasFlag(FlagsAndAttributes.FILE_ATTRIBUTE_REPARSE_POINT | FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY);

            // To treat the paths as file probes, all accesses to the path must be the probe access.
            isProbe &= access.RequestedAccess == RequestedAccess.Probe;

            // TODO: we may want to cache the result of IsIncrementalToolAccess
            if (isProbe && access.RequestedAccess == RequestedAccess.Probe && IsIncrementalToolAccess(access))
            {
                isProbe = false;
            }

            // TODO: Remove this when WDG can grog this feature with no flag.
            if (m_sandboxConfig.UnsafeSandboxConfiguration.ExistingDirectoryProbesAsEnumerations ||
                access.RequestedAccess == RequestedAccess.Enumerate)
            {
                hasEnumeration = true;
            }

            // if the access is a write on a file (that is, not on a directory), then the path is a candidate to be part of a shared opaque
            bool isPathCandidateToBeOwnedByASharedOpaque =
                access.RequestedAccess.HasFlag(RequestedAccess.Write)
                && !access.FlagsAndAttributes.HasFlag(FlagsAndAttributes.FILE_ATTRIBUTE_DIRECTORY)
                && !access.IsDirectoryCreationOrRemoval();

            // If the access is a shared opaque candidate and it was denied based on file existence, keep track of it
            if (isPathCandidateToBeOwnedByASharedOpaque && access.Method == FileAccessStatusMethod.FileExistenceBased && access.Status == FileAccessStatus.Denied)
            {
                m_result.FileExistenceDenials.Add(absolutePath);
            }

            // if the path is a candidate to be part of a shared opaque and in the cone of a shared opaque, then it is a dynamic write access
            var dynamicWriteAccesses = m_result.DynamicWriteAccesses;
            if (isPathCandidateToBeOwnedByASharedOpaque &&
                IsAccessUnderASharedOpaque(access, dynamicWriteAccesses, out AbsolutePath sharedDynamicDirectoryRoot))
            {
                bool shouldBeConsideredAsOutput = ShouldBeConsideredSharedOpaqueOutput(m_fileaccessReportingContext, access, out FileAccessAllowlist.MatchType matchType);

                if (matchType != FileAccessAllowlist.MatchType.NoMatch)
                {
                    // If the match is cacheable/uncacheable, report the access so that pip executor knows if the pip can be cached or not.
                    m_fileaccessReportingContext.AddAndReportUncacheableFileAccess(access, matchType);
                }

                if (shouldBeConsideredAsOutput)
                {
                    dynamicWriteAccesses[sharedDynamicDirectoryRoot].Add(absolutePath);
                    reportedFileAccessesAndFlags.IsSharedOpaqueOutput = true;
                }

                // This is a known output, so don't store it. If it is not a new path, we may have already added it to the sorted observations, so we need to remove it from there as well.
                if (!isNewPath)
                {
                    m_result.SortedObservationsByPath.Remove(absolutePath);
                }

                return;
            }
            // if the candidate was discarded because it was not under a shared opaque, make sure the set of denials based on file existence is also kept in sync
            else if (isPathCandidateToBeOwnedByASharedOpaque)
            {
                m_result.FileExistenceDenials.Remove(absolutePath);
            }

            // The following two lines need to be removed in order to report file accesses for
            // undeclared files and sealed directories. But since this is a breaking change, we do
            // it under an unsafe flag.
            if (m_sandboxConfig.UnsafeSandboxConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques)
            {
                // If the access occurred under any of the pip shared opaque outputs, and the access is not happening on any known input paths (neither dynamic nor static)
                // then we just skip reporting the access. Together with the above step, this means that no accesses under shared opaques that represent outputs are actually
                // reported as observed accesses. This matches the same behavior that occurs on static outputs.
                if (!m_allInputPathsUnderSharedOpaques.Contains(absolutePath)
                    && (reportedFileAccessesAndFlags.IsSharedOpaqueOutput || IsAccessUnderASharedOpaque(access, dynamicWriteAccesses, out _)))
                {
                    // Observe we don't need to update m_sortedObservations. This decision is being made solely based on the path, not the access, so we should reach the same conclusion for
                    // every access on the same path, and therefore we should never have added the path to m_sortedObservations in the first place.
                    return;
                }
            }

            // At this point the access will be counted and its flags ready to be updated
            if (isProbe)
            {
                currentFlags |= ObservationFlags.FileProbe;
            }
            else
            {
                currentFlags &= ~ObservationFlags.FileProbe;
            }

            if (isDirectoryLocation && !reportedFileAccessesAndFlags.HasDirectoryReparsePointTreatedAsFile)
            {
                currentFlags |= ObservationFlags.DirectoryLocation;
            }
            // If we have seen a directory reparse point treated as a file, make sure we clear out a potential previously set DirectoryLocation flag
            else if (reportedFileAccessesAndFlags.HasDirectoryReparsePointTreatedAsFile)
            {
                currentFlags &= ~ObservationFlags.DirectoryLocation;
            }

            if (hasEnumeration)
            {
                // If we just found out this is an enumeration, and we were keeping track of maybe unresolved absent accesses, (potentially) remove this path from there
                // since enumerations are never considered absent accesses. Enumerations are a 'sticky' flag, so once set it will always be set and we won't ever consider this path again
                if ((currentFlags & ObservationFlags.Enumeration) == 0 && EnableFullReparsePointResolving(m_configuration, m_pip))
                {
                    m_result.MaybeUnresolvedAbsentAccesses.Remove(absolutePath);
                }

                currentFlags |= ObservationFlags.Enumeration;
            }

            reportedFileAccessesAndFlags.ObservationFlags = currentFlags;
            reportedFileAccessesAndFlags.ReportedFileAccesses = reportedFileAccessesAndFlags.ReportedFileAccesses.Add(access);

            // Absent accesses may still contain reparse points. If we are fully resolving them, keep track of them for further processing
            if (EnableFullReparsePointResolving(m_configuration, m_pip) 
                && (currentFlags & ObservationFlags.Enumeration) == 0
                && reportedFileAccessesAndFlags.IsAbsentAccess) // So far all accesses were absent
            {
                if (access.Error == NativeIOConstants.ErrorPathNotFound || access.Error == NativeIOConstants.ErrorFileNotFound)
                {
                    m_result.MaybeUnresolvedAbsentAccesses.Add(absolutePath);
                }
                else
                {
                    // This is so we don't consider this path as absent access anymore
                    reportedFileAccessesAndFlags.IsAbsentAccess = false;
                    m_result.MaybeUnresolvedAbsentAccesses.Remove(absolutePath);
                }
            }

            // If this is the first time we see this path, add it to the sorted dictionary as well. Otherwise, it is already there and we just updated its value.
            if (isNewPath)
            {
                m_result.SortedObservationsByPath.Add(absolutePath, reportedFileAccessesAndFlags);
            }
        }

        private bool ShouldIncludePath(
            ReportedFileAccess reportedFileAccess,
            out AbsolutePath parsedPath)
        {
            parsedPath = AbsolutePath.Invalid;
            Contract.Assert(
                   reportedFileAccess.Status == FileAccessStatus.Allowed || reportedFileAccess.Method == FileAccessStatusMethod.FileExistenceBased,
                   "Explicitly reported accesses are defined to be successful or denied only based on file existence");

            // Enumeration probes have a corresponding Enumeration access (also explicitly reported).
            // Presently we are interested in capturing the existence of enumerations themselves rather than what was seen
            // (and for NtQueryDirectoryFile, we can't always report the individual probes anyway).
            if (reportedFileAccess.RequestedAccess == RequestedAccess.EnumerationProbe)
            {
                // If it is an incremental tool and the pip allows preserving outputs, then do not ignore because
                // the tool may depend on the directory membership.
                if (!IsIncrementalToolAccess(reportedFileAccess))
                {
                    return false;
                }
            }

            // We want an AbsolutePath for the full access. This may not be parse-able due to the accessed path
            // being invalid, or a path format we do not understand. Note that TryParseAbsolutePath logs as appropriate
            // in the latter case.
            if (!reportedFileAccess.TryParseAbsolutePath(m_context, m_loggingContext, m_pip, out parsedPath))
            {
                return false;
            }

            // Remove special accesses see Bug: #121875.
            // Some perform file accesses, which don't yet fall into any configurable file access manifest category.
            // These special tools/cases should be allowlisted, but we already have customers deployed specs without
            // using allowlists.
            if (GetSpecialCaseRulesForCoverageAndSpecialDevices(parsedPath))
            {
                return false;
            }
            else
            {
                if (AbsolutePath.TryCreate(m_context.PathTable, reportedFileAccess.Process.Path, out AbsolutePath processPath)
                    && (m_excludedToolsAndPaths.Contains((processPath, parsedPath))
                        || GetSpecialCaseRulesForSpecialTools(processPath, parsedPath)))
                {
                    m_excludedToolsAndPaths.Add((processPath, parsedPath));
                    return false;
                }
            }

            // We should exclude writes on directory paths from the accesses constructed here, which are supposed to be inputs to the pip
            // Note the similar logic below with respect to accesses on output files, but in the case of directories we just remove the
            // write operations while potentially keeping some other ones (like probes) in the observed accesses - this is because typically
            // the directories are not fully declared as outputs, so we'd rather keep track of 'input' observations on those paths.
            if (reportedFileAccess.IsDirectoryCreationOrRemoval())
            {
                return false;
            }

            if (IsEmptyOrInjectableFileAccesses(parsedPath, reportedFileAccess))
            {
                return false;
            }

            return true;
        }

        private bool IsEmptyOrInjectableFileAccesses(AbsolutePath absolutePath, ReportedFileAccess access)
        {
            if (!absolutePath.IsValid)
            {
                return true;
            }

            // Remove only entries that come from unknown or from System, or Invalid mounts.
            bool removeEntry = false;

            if (m_semanticPathExpander != null)
            {
                SemanticPathInfo semanticPathInfo = m_semanticPathExpander.GetSemanticPathInfo(absolutePath);
                removeEntry = !semanticPathInfo.IsValid || semanticPathInfo.IsSystem;
            }

            if (m_semanticPathExpander == null || removeEntry)
            {
                if (SandboxedProcessPipExecutor.IsRemovableInjectedFileAccess(absolutePath, m_pathTable))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether a reported file access should be considered as an output under a shared opaque.
        /// </summary>
        private bool ShouldBeConsideredSharedOpaqueOutput(
            FileAccessReportingContext fileAccessReportingContext,
            ReportedFileAccess access,
            out FileAccessAllowlist.MatchType matchType)
        {
            if (m_sandboxConfig.UnsafeSandboxConfiguration.DoNotApplyAllowListToDynamicOutputs())
            {
                matchType = FileAccessAllowlist.MatchType.NoMatch;
                return true;
            }

            // Given a file access f under a shared opaque.
            // - NoMatch => true
            // - MatchCacheable / NotCacheable
            //   - case 1: f is static source/output => true
            //   - case 2: f is under exclusive opaque => true
            //   - otherwise: false
            //
            // In case 1 & 2 above, f is considered an output so that the pip executor can detect for double writes.
            matchType = fileAccessReportingContext.MatchReportedFileAccess(access);
            return matchType == FileAccessAllowlist.MatchType.NoMatch
                || (access.TryParseAbsolutePath(m_context, m_loggingContext, m_pip, out AbsolutePath accessPath)
                    && (m_pipGraphFileSystemView.TryGetLatestFileArtifactForPath(accessPath).IsValid
                        || (m_pipGraphFileSystemView.IsPathUnderOutputDirectory(accessPath, out bool isSharedOpaque) && !isSharedOpaque)));
        }

        private bool IsAccessUnderASharedOpaque(
            ReportedFileAccess access,
            Dictionary<AbsolutePath, HashSet<AbsolutePath>> dynamicWriteAccesses,
            out AbsolutePath sharedDynamicDirectoryRoot)
        {
            sharedDynamicDirectoryRoot = AbsolutePath.Invalid;

            // Shortcut the search if there are no shared opaques or the manifest path
            // is invalid. For the latter, this can occur when the access happens on a location
            // the path table doesn't know about. But this means the access is not under a shared opaque.
            if (dynamicWriteAccesses.Count == 0 || !access.ManifestPath.IsValid)
            {
                return false;
            }

            // The only construct that defines a scope for detours that we allow under shared opaques is sealed directories
            // (other constructs are allowed, but they don't affect detours manifest).
            // This means we cannot directly use the manifest path to check if it is the root of a shared opaque,
            // but we can start looking up from the reported manifest path.
            // Because of bottom-up search, if a pip declares nested shared opaque directories, the innermost directory
            // wins the ownership of a produced file.

            var initialNode = access.ManifestPath.Value;

            // TODO: consider adding a cache from manifest paths to containing shared opaques. It is likely
            // that many writes for a given pip happens under the same cones.
            bool isFirstNode = true;
            foreach (var currentNode in m_context.PathTable.EnumerateHierarchyBottomUp(initialNode))
            {
                // In order to attribute accesses on shared opaque directory paths themselves to the parent
                // shared opaque directory, this will skip checking the first node when enumerating the path
                // as long as it is the same as the access 
                if (isFirstNode && access.Path == null)
                {
                    isFirstNode = false;
                    continue;
                }

                var currentPath = new AbsolutePath(currentNode);

                if (dynamicWriteAccesses.ContainsKey(currentPath))
                {
                    sharedDynamicDirectoryRoot = currentPath;
                    return true;
                }
            }

            return false;
        }

        private bool IsAccessingDirectoryLocation(AbsolutePath path, ReportedFileAccess access) =>
            // If the path is available and ends with a trailing backlash, we know that represents a directory
            (access.Path != null && access.Path.EndsWith(FileUtilities.DirectorySeparatorString, StringComparison.OrdinalIgnoreCase))
            || access.IsOpenedHandleDirectory(() => ShouldTreatDirectoryReparsePointAsFile(path, access));

        /// <summary>
        /// Returns true if a directory reparse point should be treated as a file.
        /// </summary>
        /// <remarks>
        /// CODESYNC: Public\Src\Sandbox\Windows\DetoursServices\DetouredFunctions.cpp, ShouldTreatDirectoryReparsePointAsFile
        ///           See comment there for the rationale.
        /// </remarks>
        private bool ShouldTreatDirectoryReparsePointAsFile(AbsolutePath path, ReportedFileAccess access) =>
            // Only on Windows OS.
            OperatingSystemHelper.IsWindowsOS
            // Operation does not open reparse points, or it is a write operation (e.g., creating a symlink does not pass FILE_FLAG_OPEN_REPARSE_POINT)
            && (access.FlagsAndAttributes.HasFlag(FlagsAndAttributes.FILE_FLAG_OPEN_REPARSE_POINT) || access.RequestedAccess == RequestedAccess.Write)
            // The path is not specified to be treated as a directory.
            && !m_directorySymlinksAsDirectories.Contains(path)
            // The operation is not a probe or the configuration mandates that directory symlinks are not probed as directories.
            && ((access.RequestedAccess != RequestedAccess.Probe && access.RequestedAccess != RequestedAccess.EnumerationProbe)
                || !m_sandboxConfig.UnsafeSandboxConfiguration.ProbeDirectorySymlinkAsDirectory)
            // Either full reparse point resolution is enabled, or the path is in the manifest and the policy mandates full reparse point resolution.
            && (!m_fileAccessManifest.IgnoreFullReparsePointResolving
                || (m_fileAccessManifest.TryFindManifestPathFor(path, out _, out FileAccessPolicy policy)
                    && policy.HasFlag(FileAccessPolicy.EnableFullReparsePointParsing)));

        internal bool IsIncrementalToolAccess(ReportedFileAccess access)
        {
            if (!IsIncrementalPreserveOutputPip)
            {
                return false;
            }

            if (m_incrementalToolFragments.Length == 0)
            {
                return false;
            }

            string toolPath = access.Process.Path;

            if (m_incrementalToolMatchCache.TryGetValue(toolPath, out bool result))
            {
                return result;
            }

            result = false;
            foreach (var incrementalToolSuffix in m_incrementalToolFragments)
            {
                if (toolPath.EndsWith(incrementalToolSuffix, OperatingSystemHelper.PathComparison))
                {
                    result = true;
                    break;
                }
            }

            // Cache the result
            m_incrementalToolMatchCache.AddItem(toolPath, result);
            return result;
        }

        /// <summary>
        /// Returns whether we should exclude special file access reports from processing
        /// </summary>
        /// <param name="fileAccessPath">The reported file  access path.</param>
        /// <returns>True is the file should be excluded, otherwise false.</returns>
        /// <remarks>
        /// This accounts for accesses when the FileAccessIgnoreCodeCoverage is set. A lot of our tests are running
        /// with this flag set..
        /// </remarks>
        private bool GetSpecialCaseRulesForCoverageAndSpecialDevices(
            AbsolutePath fileAccessPath)
        {
            Contract.Assert(fileAccessPath != AbsolutePath.Invalid);

            // When running test cases with Code Coverage enabled, some more files are loaded that we should ignore
            if (m_sandboxConfig.FileAccessIgnoreCodeCoverage)
            {
                string accessedPath = fileAccessPath.ToString(m_pathTable);

                if (accessedPath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                    || accessedPath.EndsWith(".nls", StringComparison.OrdinalIgnoreCase)
                    || accessedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether we should exclude special file access reports from processing
        /// </summary>
        /// <param name="processPath">Path of process that access the file.</param>
        /// <param name="fileAccessPath">The reported file  access path.</param>
        /// <returns>True is the file should be excluded, otherwise false.</returns>
        /// <remarks>
        /// Some perform file accesses, which don't yet fall into any configurable file access manifest category.
        /// These special tools/cases should be allowlisted, but we already have customers deployed specs without
        /// using allowlists.
        /// </remarks>
        private bool GetSpecialCaseRulesForSpecialTools(AbsolutePath processPath, AbsolutePath fileAccessPath)
        {
            if (m_pip.PipType != PipType.Process)
            {
                return true;
            }

            string fileName = fileAccessPath.ToString(m_pathTable);

            switch (SandboxedProcessPipExecutor.GetProcessKind(processPath, m_pip, m_pathTable))
            {
                case SpecialProcessKind.Csc:
                case SpecialProcessKind.Cvtres:
                case SpecialProcessKind.Resonexe:
                    // Some tools emit temporary files into the same directory
                    // as the final output file.
                    if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    break;

                case SpecialProcessKind.RC:
                    // The native resource compiler (RC) emits temporary files into the same
                    // directory as the final output file.
                    if (StringLooksLikeRCTempFile(fileName))
                    {
                        return true;
                    }

                    break;

                case SpecialProcessKind.Mt:
                    if (StringLooksLikeMtTempFile(fileName))
                    {
                        return true;
                    }

                    break;

                case SpecialProcessKind.CCCheck:
                case SpecialProcessKind.CCDocGen:
                case SpecialProcessKind.CCRefGen:
                case SpecialProcessKind.CCRewrite:
                    // The cc-line of tools like to find pdb files by using the pdb path embedded in a dll/exe.
                    // If the dll/exe was built with different roots, then this results in somewhat random file accesses.
                    if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    break;

                case SpecialProcessKind.WinDbg:
                case SpecialProcessKind.NotSpecial:
                    // no special treatment
                    break;
            }

            // build.exe and tracelog.dll capture dependency information in temporary files in the object root called _buildc_dep_out.<pass#>
            if (StringLooksLikeBuildExeTraceLog(fileName))
            {
                return true;
            }

            return false;
        }

        private static bool StringLooksLikeRCTempFile(string fileName)
        {
            int len = fileName.Length;
            if (len < 9)
            {
                return false;
            }

            char c1 = fileName[len - 9];
            if (c1 != '\\')
            {
                return false;
            }

            char c2 = fileName[len - 8];
            if (c2.ToUpperInvariantFast() != 'R')
            {
                return false;
            }

            char c3 = fileName[len - 7];
            if (c3.ToUpperInvariantFast() != 'C' && c3.ToUpperInvariantFast() != 'D' && c3.ToUpperInvariantFast() != 'F')
            {
                return false;
            }

            char c4 = fileName[len - 4];
            if (c4.ToUpperInvariantFast() == '.')
            {
                // RC's temp files have no extension.
                return false;
            }

            return true;
        }

        private static bool StringLooksLikeMtTempFile(string fileName)
        {
            if (!fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int beginCharIndex = fileName.LastIndexOf('\\');

            if (beginCharIndex == -1 || beginCharIndex + 3 >= fileName.Length)
            {
                return false;
            }

            char c1 = fileName[beginCharIndex + 1];
            if (c1.ToUpperInvariantFast() != 'R')
            {
                return false;
            }

            char c2 = fileName[beginCharIndex + 2];
            if (c2.ToUpperInvariantFast() != 'C')
            {
                return false;
            }

            char c3 = fileName[beginCharIndex + 3];
            if (c3.ToUpperInvariantFast() != 'X')
            {
                return false;
            }

            return true;
        }

        private static bool StringLooksLikeBuildExeTraceLog(string fileName)
        {
            // detect filenames of the following form
            // _buildc_dep_out.pass<NUMBER>
            int len = fileName.Length;

            int trailingDigits = 0;
            for (; len > 0 && fileName[len - 1] >= '0' && fileName[len - 1] <= '9'; len--)
            {
                trailingDigits++;
            }

            if (trailingDigits == 0)
            {
                return false;
            }

            return fileName.EndsWith("_buildc_dep_out.pass", StringComparison.OrdinalIgnoreCase);
        }
    }
}