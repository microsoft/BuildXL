// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Tracing;
using BuildXL.Native.IO;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.BuildParameters;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// Tracks inputs that impact the pip graph (parsing and evaluation)
    /// </summary>
    /// <remarks>
    /// Tracks the following:
    /// Input files - config, spec, module files
    /// Assemblies referenced by bxl.exe (won't cache across BuildXL builds)
    /// Partial evaluation (filtering based on resolving values)
    /// Environment variables
    /// Command line environment variable overrides
    /// Qualifiers
    /// TODO:  Make EnvironmentVariables and paths machine independent
    /// </remarks>
    public sealed class InputTracker
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "InputTracker", version: 5);

        /// <summary>
        /// If set this will cause there to always be a graph cache miss. This is an operational escape hatch in case
        /// the cached graph is somehow corrupt and needs to be overwritten.
        /// </summary>
        internal const string ForceInvalidateCachedGraphVariable = "ForceInvalidateCachedGraph";

        /// <summary>
        /// Unset environment variable value.
        /// </summary>
        public const string UnsetVariableMarker = "[[UnsetEnvironmentVariable]]";

        /// <summary>
        /// Whether this InputTracker is enabled
        /// </summary>
        internal bool IsEnabled { get; }

        /// <summary>
        /// The FileChangeTracker in use
        /// </summary>
        public FileChangeTracker FileChangeTracker { get; }

        /// <summary>
        /// Returns paths of files that did not change since the last run, or <code>null</code> if no such information is available.
        /// <seealso cref="BuildXL.FrontEnd.Sdk.FrontEndEngineAbstraction.GetUnchangedFiles"/>
        /// </summary>
        public IEnumerable<string> UnchangedFiles => m_unchangedPaths?.Keys;

        /// <summary>
        /// Returns paths of files that definitely changed since the previous run.
        /// <seealso cref="BuildXL.FrontEnd.Sdk.FrontEndEngineAbstraction.GetChangedFiles"/>
        /// </summary>
        public IEnumerable<string> ChangedFiles
        {
            get
            {
                Contract.Assert(
                    m_changedPaths == null || m_isChangePathSetComplete,
                    "This InputTracker did not remember all changed files.  If you want a complete set of changed files, " +
                    "configure BuildXL engine appropriately when calling BuildXLEngine.Create()");

                return m_changedPaths;
            }
        }

        /// <summary>
        /// Hashes of inputs.
        /// </summary>
        public IDictionary<string, ContentHash> InputHashes => m_inputHashes;

        /// <summary>
        /// Directory fingerprints.
        /// </summary>
        public IDictionary<string, DirectoryMembershipTrackingFingerprint> DirectoryFingerprints => m_directoryFingerprints;

        private readonly FileContentTable m_fileContentTable;
        private readonly ConcurrentDictionary<string, ContentHash> m_inputHashes = new ConcurrentDictionary<string, ContentHash>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ContentHash> m_unchangedPaths;
        private readonly ConcurrentQueue<string> m_changedPaths;
        private readonly bool m_isChangePathSetComplete;

        private readonly ConcurrentDictionary<string, DirectoryMembershipTrackingFingerprint> m_directoryFingerprints =
            new ConcurrentDictionary<string, DirectoryMembershipTrackingFingerprint>(StringComparer.OrdinalIgnoreCase);

        private bool m_allDirectoriesEnumerationsAccountedFor = true;
        private bool m_wasAnyDirectoryEnumerated;

        private readonly LoggingContext m_loggingContext;
        private const int MaxReportedChangedInputFiles = 10;

        private InputTracker(
            LoggingContext loggingContext,
            FileContentTable fileContentTable,
            CompositeGraphFingerprint graphFingerprint,
            FileChangeTracker tracker,
            bool isEnabled,
            InputChanges inputChanges = null)
        {
            Contract.Requires(loggingContext != null);

            m_loggingContext = loggingContext;
            m_fileContentTable = fileContentTable;
            GraphFingerprint = graphFingerprint;
            FileChangeTracker = tracker;
            IsEnabled = isEnabled;

            if (inputChanges != null)
            {
                // The FileChangeTracker on unchangedInputs may be disabled, but the collection of unchangedPaths may still be
                // used since a disabled FileChangeTracker wouldn't need accesses registered anyway
                FileChangeTracker = inputChanges.FileChangeTracker;
                m_unchangedPaths = inputChanges.UnchangedPaths;
                m_changedPaths = inputChanges.ChangedPaths;
                m_isChangePathSetComplete = inputChanges.IsChangedPathSetComplete;
            }
        }

        /// <summary>
        /// Creates an instance of disabled <see cref="InputTracker"/>.
        /// </summary>
        public static InputTracker CreateDisabledTracker(LoggingContext loggingContext)
        {
            Contract.Requires(loggingContext != null);

            return new InputTracker(loggingContext, null, CompositeGraphFingerprint.Zero, FileChangeTracker.CreateDisabledTracker(loggingContext), false);
        }

        /// <summary>
        /// Creates an instance of <see cref="InputTracker"/>.
        /// </summary>
        /// <remarks>
        /// If journal is enabled, then the resulting instance supports fast up-to-date checks using a <see cref="BuildXL.Storage.ChangeTracking.FileChangeTracker"/>.
        /// </remarks>
        public static InputTracker Create(
            LoggingContext loggingContext,
            FileContentTable fileContentTable,
            JournalState journalState,
            CompositeGraphFingerprint graphFingerprint)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(fileContentTable != null);

            return new InputTracker(
                loggingContext,
                fileContentTable,
                graphFingerprint,
                journalState.IsEnabled
                    ? FileChangeTracker.StartTrackingChanges(loggingContext, journalState.VolumeMap, journalState.Journal, graphFingerprint.BuildEngineHash.ToString())
                    : FileChangeTracker.CreateDisabledTracker(loggingContext),
                true);
        }

        /// <summary>
        /// Creates an instance of <see cref="InputTracker"/> by continuing from existing one with input changes.
        /// </summary>
        /// <remarks>
        /// The existing tracker may
        /// be tracking extra files that are not truly inputs to this run. That is ok because the actual inputs
        /// we care about are explicitly marked. Changes to files in registered to the tracker that are not relevant
        /// will be ignored.
        /// </remarks>
        public static InputTracker ContinueExistingTrackerWithInputChanges(
            LoggingContext loggingContext,
            FileContentTable fileContentTable,
            InputChanges inputChanges,
            CompositeGraphFingerprint graphFingerprint)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(inputChanges != null);

            return new InputTracker(loggingContext, fileContentTable, graphFingerprint, null, true, inputChanges);
        }

        /// <summary>
        /// Returns the fingerprint associated with the graph being tracked.
        /// </summary>
        public CompositeGraphFingerprint GraphFingerprint { get; }

        /// <summary>
        /// Registers an accessed input of the build graph
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000", Justification = "Created memory streams are owned by return value.")]
        public void RegisterFileAccess(AbsolutePath path, PathTable pathTable)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(pathTable != null);

            if (IsEnabled)
            {
                string expandedPath = path.ToString(pathTable);
                RegisterFileAccess(expandedPath);
            }
        }

        /// <summary>
        /// Registers an accessed input of the build graph
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000", Justification = "Created memory streams are owned by return value.")]
        public void RegisterFileAccess(string path)
        {
            if (IsEnabled)
            {
                using (
                    FileStream fs = FileUtilities.CreateFileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Delete | FileShare.Read))
                {
                    FileContentTableExtensions.VersionedFileIdentityAndContentInfoWithOrigin identityAndContentInfoWithOrigin =
                        m_fileContentTable.GetAndRecordContentHashAsync(fs).GetAwaiter().GetResult();
                    RegisterFileAccess(fs.SafeFileHandle, path, identityAndContentInfoWithOrigin.VersionedFileIdentityAndContentInfo);
                }
            }
        }

        /// <summary>
        /// Tracks directory.
        /// </summary>
        public bool TrackDirectory(
            string directoryPath,
            IReadOnlyList<(string, FileAttributes)> members)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryPath));
            Contract.Requires(members != null);

            return TrackDirectoryInternal(directoryPath, members);
        }

        /// <summary>
        /// Tracks directory.
        /// </summary>
        public bool TrackDirectory(string directoryPath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryPath));
            return TrackDirectoryInternal(directoryPath, null);
        }

        /// <summary>
        /// Checks whether the file or directory exists on disk and tracks the probing operation.
        /// </summary>
        public PathExistence? ProbeFileOrDirectoryExistence(string path)
        {
            Analysis.IgnoreResult(
                FileChangeTracker.TryProbeAndTrackPath(path)
            );
            var result = FileUtilities.TryProbePathExistence(path, followSymlink: true);
            if (!result.Succeeded)
            {
                return null;
            }

            bool exists = result.Result != PathExistence.Nonexistent;

            // if file or directory does not exist we need to register absent file probe
            if (!exists)
            {
                RegisterFile(path, WellKnownContentHashes.AbsentFile);
            }
            else
            {
                // Even if the file or directory exists, we have to hash the content
                // to prevent the graph cache miss for cloud build scenarios.
                if (result.Result == PathExistence.ExistsAsFile)
                {
                    RegisterFile(path, WellKnownContentHashes.ExistentFileProbe);
                }
                else
                {
                    Analysis.IgnoreResult(
                        TryComputeDirectoryMembershipFingerprint(path)
                    );
                }
            }

            return result.Result;
        }

        private bool TrackDirectoryInternal(string directoryPath, IReadOnlyList<(string, FileAttributes)> members)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryPath));

            m_wasAnyDirectoryEnumerated = true;

            Possible<FileChangeTrackingSet.EnumerationResult> possibleEnumResult;

            if (members != null)
            {
                possibleEnumResult = FileChangeTracker.TryTrackDirectoryMembership(directoryPath, members);
            }
            else
            {
                // TODO: Right now, DScript enumerates a directory and then calls this method to track the directory.
                // In future, this method will be responsible for enumeration as well.
                possibleEnumResult = FileChangeTracker.TryEnumerateDirectoryAndTrackMembership(
                    directoryPath,
                    (entryName, entryAttributes) => { });
            }

            Possible<DirectoryMembershipTrackingFingerprint> possiblyDirectoryFingerprint;
            if (!possibleEnumResult.Succeeded)
            {
                possiblyDirectoryFingerprint = TryComputeDirectoryMembershipFingerprint(directoryPath, members);
            }
            else
            {
                possiblyDirectoryFingerprint = possibleEnumResult.Result.Fingerprint;
            }

            if (!possiblyDirectoryFingerprint.Succeeded)
            {
                m_allDirectoriesEnumerationsAccountedFor = false;

                Scheduler.Tracing.Logger.Log.DirectoryFingerprintingFilesystemEnumerationFailed(
                    m_loggingContext,
                    directoryPath,
                    possibleEnumResult.Failure.DescribeIncludingInnerFailures());
                
                return false;
            }

            m_directoryFingerprints.AddOrUpdate(
                directoryPath,
                possiblyDirectoryFingerprint.Result,
                (oldPath, oldFingerprint) => possiblyDirectoryFingerprint.Result);

            return true;            
        }

        private static Possible<DirectoryMembershipTrackingFingerprint> TryComputeDirectoryMembershipFingerprint(
            FileChangeTracker tracker,
            string directoryPath)
        {
            Contract.Requires(tracker != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryPath));

            var possibleEnumResult = tracker.TryEnumerateDirectoryAndTrackMembership(
                    directoryPath,
                    (entryName, entryAttributes) => { });

            if (possibleEnumResult.Succeeded)
            {
                return possibleEnumResult.Result.Fingerprint;
            }

            var possibleDirectoryFingerprint = TryComputeDirectoryMembershipFingerprint(directoryPath, null);

            if (!possibleDirectoryFingerprint.Succeeded)
            {
                return possibleDirectoryFingerprint.Failure;
            }

            return possibleDirectoryFingerprint;
        }

        /// <summary>
        /// Computes directory membership fingerprint.
        /// </summary>
        public static Possible<DirectoryMembershipTrackingFingerprint> TryComputeDirectoryMembershipFingerprint(
            string directoryPath,
            IReadOnlyList<(string, FileAttributes)> members)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryPath));

            if (members != null)
            {
                return DirectoryMembershipTrackingFingerprinter.ComputeFingerprint(members);
            }

            var possibleFingerprint = DirectoryMembershipTrackingFingerprinter.ComputeFingerprint(directoryPath);
            if (!possibleFingerprint.Succeeded)
            {
                return possibleFingerprint.Failure;
            }

            return possibleFingerprint.Result.Fingerprint;
        }

        /// <summary>
        /// Computes directory membership fingerprint.
        /// </summary>
        public Possible<DirectoryMembershipTrackingFingerprint> TryComputeDirectoryMembershipFingerprint(string directoryPath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryPath));

            DirectoryMembershipTrackingFingerprint fingerprint;
            return !m_directoryFingerprints.TryGetValue(directoryPath, out fingerprint)
                ? TryComputeDirectoryMembershipFingerprint(directoryPath, null)
                : fingerprint;
        }

        /// <summary>
        /// Gets <see cref="ContentHash"/> of input file known to this instance of <see cref="InputTracker"/>.
        /// </summary>
        public bool TryGetHashForKnownInputFile(string path, out ContentHash contentHash) => m_inputHashes.TryGetValue(path, out contentHash);

        /// <summary>
        /// Registers an accessed input of the build graph. The file should already be tracked in the associated <see cref="FileContentTable"/>.
        /// </summary>
        public void RegisterFileAccess(SafeFileHandle handle, string path, in VersionedFileIdentityAndContentInfo identityAndContentInfo)
        {
            Analysis.IgnoreResult(FileChangeTracker.TryTrackChangesToFile(handle, path, identityAndContentInfo.Identity));
            RegisterFile(path, identityAndContentInfo.FileContentInfo.Hash);
        }

        /// <summary>
        /// Gets a hash for a file if it is known
        /// </summary>
        public bool TryGetHashForUnchangedFile(string path, out ContentHash hash)
        {
            if (m_unchangedPaths != null && m_unchangedPaths.TryGetValue(path, out hash))
            {
                return true;
            }

            hash = ContentHashingUtilities.ZeroHash;

            return false;
        }

        /// <summary>
        /// Registers access to an already tracked file.
        /// retrieved from <see cref="TryGetHashForUnchangedFile"/>
        /// </summary>
        public void RegisterAccessToTrackedFile(string path, ContentHash hash)
        {
            Contract.Assume(
                m_unchangedPaths != null && m_unchangedPaths.ContainsKey(path),
                "File was not already tracked. It may not be registered with this method");

            RegisterFile(path, hash);
        }

        private void RegisterFile(string path, ContentHash hash)
        {
            if (IsEnabled)
            {
                // Note that we allow visiting the same path multiple times so long as it has the same hash:
                // - two resolvers share the same module.
                // Observe that WellKnownContentHashes.ExistentFileProbe is used as a marker to state a path was probed, but its 
                // content hash is not necessarily known. This means the allowed transitions (for the same path) are characterized as follows:
                // - An ExistentFileProbe can turn into a real content hash, in the case the path is first probed and then read (and in that case the real hash is kept for that moment on)
                // - An ExistentFileProbe cannot turn into an AbsentFile, and vice-versa.
                // - A path with a real content hash can later be registered as an existent probe (but the real content hash should be kept)
                m_inputHashes.AddOrUpdate(
                    path,
                    hash,
                    (existingPath, existingHash) =>
                    {
                        if (existingHash == WellKnownContentHashes.ExistentFileProbe)
                        {
                            // The path cannot be registered first as present and later as absent
                            if (hash == WellKnownContentHashes.AbsentFile)
                            {
                                Contract.Assert(false, I($"Input file '{path}' is registered multiple times with inconsistent presence on disk. It is being registered as an absent file, but it was previously registered as a probe to a present file"));
                            }

                            // This is the case where the new hash is either ExistentFileProbe (and nothing really changed) or the path is now actually being read. For the latter, observe that we replace the marker for 'present file was probed'
                            // with the actual content hash
                            return hash;
                        }

                        // If this time the file is registered as an existent probe, but previously it was registered with an actual content hash,
                        // that's ok, but we need to keep the real content hash. Observe at this point we know the existing hash is not a probe to an existing file.
                        if (hash == WellKnownContentHashes.ExistentFileProbe && existingHash != WellKnownContentHashes.AbsentFile)
                        {
                            return existingHash;
                        }

                        if (existingHash != hash)
                        {
                            ContentHash actualHash = WellKnownContentHashes.AbsentFile;
                            if (File.Exists(existingPath))
                            {
                                actualHash = ContentHashingUtilities.HashFileAsync(existingPath).GetAwaiter().GetResult();
                            }

                            string actualHashString = actualHash.ToString();
                            string specCacheHashString = "<UNKNOWN>";

                            if (TryGetHashForUnchangedFile(path, out ContentHash specCacheHash))
                            {
                                specCacheHashString = specCacheHash.ToString();
                            }

                            Contract.Assert(false, I($"Input file '{path}' is encountered multiple times with different hashes: Existing hash: {existingHash} | New hash: {hash} | Actual hash: {actualHashString} | Spec cache hash: {specCacheHashString}"));
                        }

                        return existingHash;
                    });
            }
        }

        /// <summary>
        /// Writes the consumed inputs to a file for consumption in next build.
        /// <paramref name="changeTrackingStatePath"/> is always required, but only populated if change tracking is enabled (journal provided on construction).
        /// </summary>
        public void WriteToFile(
            BinaryWriter writer,
            PathTable pathTable,
            IReadOnlyDictionary<string, string> buildParametersImpactingBuild,
            IReadOnlyDictionary<string, IMount> mountsImpactingBuild,
            string changeTrackingStatePath)
        {
            Contract.Requires(writer != null);
            Contract.Requires(changeTrackingStatePath != null);

            if (!IsEnabled)
            {
                return;
            }

            // Writes the fingerprint hash
            // ReSharper disable once ImpureMethodCallOnReadonlyValueField
            GraphFingerprint.Serialize(writer);

            // Writes whether the globbing is used during initializing resolvers or evaluating the DS specs
            writer.Write(m_wasAnyDirectoryEnumerated);

            // Writes whether we can trust the tracked directory enumerations. If this is false, something went wrong
            // with using the change tracker in the previous build and we don't have fingerprints and hence cannot check
            // for a graph hit on the subsequent build.
            writer.Write(m_allDirectoriesEnumerationsAccountedFor);

            // Write the environment variables that impact the build
            if (buildParametersImpactingBuild == null)
            {
                writer.Write(0);
            }
            else
            {
                writer.Write(buildParametersImpactingBuild.Count);
                {
                    foreach (var kvp in buildParametersImpactingBuild)
                    {
                        writer.Write(kvp.Key);
                        writer.Write(NormalizeEnvironmentVariableValue(kvp.Value));
                    }
                }
            }

            // Write the mounts that impact the build
            if (mountsImpactingBuild == null)
            {
                writer.Write(0);
            }
            else
            {
                writer.Write(mountsImpactingBuild.Count);
                {
                    foreach (var kvp in mountsImpactingBuild)
                    {
                        writer.Write(kvp.Key);
                        writer.Write(kvp.Value.Path.ToString(pathTable));
                    }
                }
            }

            // Write out the change-tracker state if it is usable, so that it can be used to skip individual file content hash checks.
            // We use atomic save token to keep input tracker and file change tracker in-sync. However, for some frontend optimization,
            // the file change tracker from previous input tracker may get leaked to the current input tracker. One case is the following:
            // Build 1:
            // - MatchesReader returns no match (user may modifies some spec file).
            // - But the graph is obtained from the cache.
            // - The input tracker is replace with the one from the graph.
            // Build 2:
            // - Input tracker is not sync with file change tracker, thus a new file change tracker is created, with the same token as the input tracker.
            // - MatchesReader returns no match (user may again modifies some spec file).
            // - No graph can be obtained from the cache.
            // - Spec is evaluated with a new input tracker, but with the same file change tracker that was newly created before.
            // - Now, file change tracker has the token of previous input tracker, but we have a new input tracker.
            // To handle this case, because at this point we have a new input tracker, then we create a new token, and overrides the token
            // in file change tracker.
            FileEnvelopeId atomicSaveToken = FileEnvelopeId.Create();
            FileEnvelopeId fileEnvelopeIdToSaveWith = FileChangeTracker.GetFileEnvelopeToSaveWith(overrideFileEnvelopeId: atomicSaveToken);
            FileChangeTracker.SaveTrackingStateIfChanged(changeTrackingStatePath, fileEnvelopeIdToSaveWith);
            atomicSaveToken.Serialize(writer);

            // Write the input files. Sort by filename so when they are read to check for graph reuse, they can be queued
            // in an order more optimal for disk access when checking that the file hashes match.
            writer.Write(m_inputHashes.Count);
            foreach (var file in m_inputHashes.Select(kvp => (kvp.Key, kvp.Value)).OrderBy(kvp => kvp.Item1))
            {
                writer.Write(file.Item1);
                file.Item2.SerializeHashBytes(writer);
            }

            writer.Write(m_directoryFingerprints.Count);
            foreach (
                var directory in
                    m_directoryFingerprints.Select(kvp => (kvp.Key, kvp.Value)).OrderBy(kvp => kvp.Item1))
            {
                writer.Write(directory.Item1);
                directory.Item2.Hash.SerializeHashBytes(writer);
            }
        }

        /// <summary>
        /// Deserializes input tracker and writes it as text.
        /// </summary>
        /// <remarks>
        /// We never create an input tracker from its serialized form. In <see cref="MatchesReader(LoggingContext, BinaryReader, FileContentTable, JournalState, TimeSpan?, string, IBuildParameters, MountsTable, Engine.GraphFingerprint, int, IConfiguration, bool)"/>
        /// we do both serialization and input matching simultaneously. This method is only intended for analyzer to read and print the content of input tracker.
        /// </remarks>
        public static void ReadAndWriteText(BinaryReader reader, TextWriter writer)
        {
            CompositeGraphFingerprint previousFingerprint = CompositeGraphFingerprint.Deserialize(reader);
            previousFingerprint.WriteText(writer);
            writer.WriteLine(I($"Was any directory enumerated: {reader.ReadBoolean()}"));
            writer.WriteLine(I($"All directories are accounted: {reader.ReadBoolean()}"));

            int buildParamCount = reader.ReadInt32();

            if (buildParamCount > 0)
            {
                var envVarList = new List<(string, string)>();
                writer.WriteLine("Environment variables: ");

                for (int i = 0; i < buildParamCount; ++i)
                {
                    envVarList.Add((reader.ReadString(), reader.ReadString()));
                }

                foreach (var envVar in envVarList.OrderBy(tuple => tuple.Item1, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WriteLine(I($"\t - ({envVar.Item1}, {envVar.Item2})"));
                }
            }

            int mountsImpactingBuildCount = reader.ReadInt32();

            if (mountsImpactingBuildCount > 0)
            {
                var mountList = new List<(string, string)>();
                writer.WriteLine("Mounts: ");

                for (int i = 0; i < mountsImpactingBuildCount; ++i)
                {
                    mountList.Add((reader.ReadString(), reader.ReadString()));
                }

                foreach (var envVar in mountList.OrderBy(tuple => tuple.Item1, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WriteLine(I($"\t - ({envVar.Item1}, {envVar.Item2})"));
                }
            }

            writer.WriteLine(I($"Atomic save token: {FileEnvelopeId.Deserialize(reader).ToString()}"));

            int inputHashCount = reader.ReadInt32();

            if (inputHashCount > 0)
            { 
                var inputHashList = new List<(string, ContentHash)>();
                writer.WriteLine("Input file hashes: ");

                for (int i = 0; i < inputHashCount; ++i)
                {
                    inputHashList.Add((reader.ReadString(), ContentHashingUtilities.CreateFrom(reader)));
                }

                foreach (var input in inputHashList.OrderBy(tuple => tuple.Item1, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WriteLine(I($"\t - {input.Item1}: {input.Item2.ToString()}"));
                }
            }

            int directoryHashCount = reader.ReadInt32();

            if (directoryHashCount > 0)
            {
                var directoryHashList = new List<(string, ContentHash)>();
                writer.WriteLine("Directory membership hashes: ");

                for (int i = 0; i < directoryHashCount; ++i)
                {
                    directoryHashList.Add((reader.ReadString(), ContentHashingUtilities.CreateFrom(reader)));
                }

                foreach (var input in directoryHashList.OrderBy(tuple => tuple.Item1, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WriteLine(I($"\t - {input.Item1}: {input.Item2.ToString()}"));
                }
            }
        }

        /// <summary>
        /// For DScript, if graph cache is missed, we need to create a new set of directory fingerprints.
        /// </summary>
        internal void ClearDirectoryFingerprints()
        {
            FileChangeTracker.ClearDirectoryFingerprints();
        }

        /// <summary>
        /// Checks to see if the current set of inputs (files, effective environment variables, etc.)
        /// are compatible with a previously-recorded 'PreviousInputs' file (saved via <see cref="WriteToFile"/>).
        /// The provided <paramref name="journalState"/> may be used for faster verification of previous inputs; this may update
        /// state at <paramref name="changeTrackingStatePath"/> and so requires write access.
        /// </summary>
        /// <remarks>
        /// On I/O failures, the check is aborted with a warning (as if there was a mismatched input).
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public static MatchResult CheckIfAvailableInputsMatchPreviousRun(
            LoggingContext loggingContext,
            EngineSerializer serializer,
            FileContentTable fileContentTable,
            IBuildParameters availableEnvironmentVariables,
            MountsTable availableMounts,
            GraphFingerprint graphFingerprint,
            int maxDegreeOfParallelism,
            IConfiguration configuration,
            bool checkAllPossiblyChangedPaths,
            JournalState journalState,
            TimeSpan? timeLimitForJournalScanning = null,
            string changeTrackingStatePath = null)
        {
            Contract.Requires(serializer != null);
            Contract.Requires(fileContentTable != null);
            Contract.Requires(availableEnvironmentVariables != null);
            Contract.Requires(journalState.IsDisabled || !string.IsNullOrWhiteSpace(changeTrackingStatePath));

            if (Environment.GetEnvironmentVariable(ForceInvalidateCachedGraphVariable) != null)
            {
                return new MatchResult { MissType = GraphCacheMissReason.ForcedMiss };
            }

            var missType = GraphCacheMissReason.NoPreviousRunToCheck;
            if (File.Exists(serializer.GetFullPath(GraphCacheFile.PreviousInputs)))
            {
                try
                {
                    var result = serializer.DeserializeFromFileAsync(
                        GraphCacheFile.PreviousInputs,
                        reader => Task.FromResult(MatchesReader(
                                    loggingContext,
                                    reader,
                                    fileContentTable,
                                    journalState,
                                    timeLimitForJournalScanning,
                                    changeTrackingStatePath,
                                    availableEnvironmentVariables,
                                    availableMounts,
                                    graphFingerprint,
                                    maxDegreeOfParallelism,
                                    configuration,
                                    checkAllPossiblyChangedPaths)),
                        skipHeader: true).GetAwaiter().GetResult();

                    if (result.HasValue)
                    {
                        return result.Value;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.FailedToDeserializePreviousInputs(loggingContext, ex.GetLogEventMessage());
                }

                missType = GraphCacheMissReason.CheckFailed;
            }

            return new MatchResult { MissType = missType };
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "ms")]
        private static MatchResult? MatchesReader(
            LoggingContext loggingContext,
            BinaryReader reader,
            FileContentTable fileContentTable,
            JournalState journalState,
            TimeSpan? timeLimitForJournalScanning,
            string changeTrackingStatePath,
            IBuildParameters availableEnvironmentVariables,
            MountsTable availableMounts,
            GraphFingerprint graphFingerprint,
            int maxDegreeOfParallelism,
            IConfiguration configuration,
            bool checkAllPossiblyChangedPaths)
        {
            Contract.Requires(journalState.IsDisabled || !string.IsNullOrWhiteSpace(changeTrackingStatePath));
            Contract.Requires(graphFingerprint != null);

            MatchResult result = default;
            ContentFingerprint? matchingFingerprint;

            // Step 1: Check the fingerprint contributing to the set of input files.
            CompositeGraphFingerprint previousFingerprint = CompositeGraphFingerprint.Deserialize(reader);
            GraphCacheMissReason missReasonForCompatibleGraphFingerprint;
            GraphCacheMissReason missReasonForExactGraphFingerprint;

            if ((missReasonForCompatibleGraphFingerprint = previousFingerprint.CompareFingerprint(graphFingerprint.CompatibleFingerprint)) == GraphCacheMissReason.NoMiss)
            {
                Logger.Log.MatchedCompatibleGraphFingerprint(loggingContext);
                matchingFingerprint = graphFingerprint.CompatibleFingerprint.OverallFingerprint;
            }
            else if ((missReasonForExactGraphFingerprint = previousFingerprint.CompareFingerprint(graphFingerprint.ExactFingerprint)) == GraphCacheMissReason.NoMiss)
            {
                Logger.Log.MatchedExactGraphFingerprint(loggingContext);
                matchingFingerprint = graphFingerprint.ExactFingerprint.OverallFingerprint;
            }
            else
            {
                // Both exact and compatible fingerprints are different from the one in the previous input tracker.
                result.MissType = missReasonForCompatibleGraphFingerprint;

                Logger.Log.InputTrackerHasMismatchedGraphFingerprint(loggingContext, missReasonForCompatibleGraphFingerprint.ToString(), missReasonForCompatibleGraphFingerprint.ToString());
                return result;
            }

            Contract.Assert(matchingFingerprint.HasValue);
            result.MatchingFingerprint = matchingFingerprint.Value;

            // Step 2: Check if all directory enumerations are accounted.
            bool wasAnyDirectoryEnumerated = reader.ReadBoolean();
            bool allDirectoriesAccountedFor = reader.ReadBoolean();
            if (!allDirectoriesAccountedFor)
            {
                result.MissType = GraphCacheMissReason.NotAllDirectoryEnumerationsAreAccounted;
                Logger.Log.InputTrackerHasUnaccountedDirectoryEnumeration(loggingContext);
                return result;
            }

            // Step 3: Check if all environment variables in the previous input tracker match with the current ones.
            int environmentVariableCount = reader.ReadInt32();
            for (int i = 0; i < environmentVariableCount; i++)
            {
                string key = reader.ReadString();
                string previousValue = reader.ReadString();

                if (availableEnvironmentVariables.ContainsKey(key))
                {
                    string currentValue = NormalizeEnvironmentVariableValue(availableEnvironmentVariables[key]);
                    if (!previousValue.Equals(currentValue, StringComparison.OrdinalIgnoreCase))
                    {
                        result.MissType = GraphCacheMissReason.EnvironmentVariableChanged;
                        result.FirstMissIdentifier = string.Format(
                            CultureInfo.InvariantCulture,
                            Strings.InputTracker_EnvironmentVariableChanged,
                            key,
                            previousValue,
                            currentValue);
                        Logger.Log.InputTrackerDetectedEnvironmentVariableChanged(loggingContext, key, previousValue, currentValue);
                        return result;
                    }
                }
                else
                {
                    if (previousValue != UnsetVariableMarker)
                    {
                        // The previously consumed environment variable is not known and was not previously unset.
                        result.MissType = GraphCacheMissReason.EnvironmentVariableChanged;
                        result.FirstMissIdentifier = string.Format(CultureInfo.InvariantCulture, Strings.InputTracker_EnvironmentVariableRemoved, key);
                        Logger.Log.InputTrackerDetectedEnvironmentVariableChanged(loggingContext, key, previousValue, UnsetVariableMarker);
                        return result;
                    }
                }
            }

            // Step 4: Check if all mounts in the previous input tracker match with the current ones.
            var availableMountsByName = availableMounts.MountsByName;
            int mountCount = reader.ReadInt32();
            for (int i = 0; i < mountCount; i++)
            {
                string mountName = reader.ReadString();
                string previousPath = reader.ReadString();

                if (availableMountsByName.ContainsKey(mountName))
                {
                    string currentPath = availableMountsByName[mountName].Path.ToString(availableMounts.MountPathExpander.PathTable);
                    if (!previousPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        result.MissType = GraphCacheMissReason.MountChanged;
                        result.FirstMissIdentifier = string.Format(
                            CultureInfo.InvariantCulture,
                            Strings.InputTracker_MountChanged,
                            mountName,
                            previousPath,
                            currentPath);
                        Logger.Log.InputTrackerDetectedMountChanged(loggingContext, mountName, previousPath, currentPath);
                        return result;
                    }
                }
                else
                {
                    // The previously used mount is not known
                    result.MissType = GraphCacheMissReason.MountChanged;
                    result.FirstMissIdentifier = string.Format(CultureInfo.InvariantCulture, Strings.InputTracker_MountRemoved, mountName);
                    Logger.Log.InputTrackerDetectedMountChanged(loggingContext, mountName, previousPath, string.Empty);
                    return result;
                }
            }

            // Step 5: Check if input files in the input tracker match the actual ones using journal scan and, if needed, checking content hashes.

            // The previous run may have had journal-based change tracking enabled. If so, we can try to establish the absence
            // of changes without checking individual files.
            // Note that we always have an atomic save token, even if tracking wasn't originally enabled; this allows re-generation
            // (or eventual initial generation) of change tracking information for existing persisted graphs.
            // 
            // Basically the atomic save token keeps the input tracker and the underlying file change tracker in sync. 
            // Suppose that the input tracker (i.e., previous inputs file) comes from the content cache after a graph cache hit.
            // Because previous journal checkpoint file is never stored to the content cache, we may have an existing previous journal
            // checkpoint file from previous build. However, that journal file is no longer valid. The save token guards the input tracker
            // from using the journal checkpoint file.
            FileEnvelopeId atomicSaveToken = FileEnvelopeId.Deserialize(reader);

            if (journalState.IsDisabled)
            {
                // Journal scan is not enabled. Check that the content hashes of input files recorded in the previous
                // input tracker match the actual ones.
                var disabledTracker = FileChangeTracker.CreateDisabledTracker(loggingContext);
                return MatchesInputFiles(
                    loggingContext,
                    reader,
                    disabledTracker,
                    fileContentTable,
                    matchingFingerprint.Value,
                    maxDegreeOfParallelism,
                    configuration.Logging,
                    checkAllPossiblyChangedPaths);
            }

            // Journal scan is enabled and will be used to detect changes.
            FileChangeTracker fileChangeTracker;
            LoadingTrackerResult loadingTrackerResult = null;

            // Load or start a new file change tracker.
            if (configuration.Engine.FileChangeTrackerInitializationMode == FileChangeTrackerInitializationMode.ForceRestart)
            {
                fileChangeTracker = FileChangeTracker.StartTrackingChanges(
                    loggingContext, 
                    journalState.VolumeMap, 
                    journalState.Journal, 
                    graphFingerprint.ExactFingerprint.BuildEngineHash.ToString(),
                    atomicSaveToken); // Passing the save token ensure that the file change tracker is owned by (or correlated to) the input tracker.
            }
            else
            {
                loadingTrackerResult = FileChangeTracker.ResumeOrRestartTrackingChanges(
                    loggingContext,
                    journalState.VolumeMap,
                    journalState.Journal,
                    changeTrackingStatePath,
                    graphFingerprint.ExactFingerprint.BuildEngineHash.ToString(),
                    out fileChangeTracker);
            }

            bool shouldMatchInputFiles = true;
            GraphInputArtifactChanges graphInputArtifactChanges = null;

            if (loadingTrackerResult != null && loadingTrackerResult.Succeeded && fileChangeTracker.IsTrackingChanges)
            {
                // Loading the file change tracker succeeded, and the loaded file change tracker is in the tracking mode.
                if (atomicSaveToken == fileChangeTracker.FileEnvelopeId)
                {
                    graphInputArtifactChanges = new GraphInputArtifactChanges(loggingContext);
                }
                else
                {
                    // But unfortunately, the loaded file change tracker is not in-sync with the input tracker.
                    // File change tracker is owned by the input tracker. To keep them in-sync, we need to create a new file
                    // change tracker.
                    fileChangeTracker = FileChangeTracker.StartTrackingChanges(
                        loggingContext,
                        journalState.VolumeMap,
                        journalState.Journal,
                        graphFingerprint.ExactFingerprint.BuildEngineHash.ToString(),
                        atomicSaveToken); // Passing the save token ensure that the file change tracker is owned by (or correlated to) the input tracker.

                    Logger.Log.GraphInputArtifactChangesTokensMismatch(
                        loggingContext,
                        atomicSaveToken.ToString(),
                        fileChangeTracker.FileEnvelopeId.ToString());
                }

                if (fileChangeTracker.IsTrackingChanges)
                {
                    // Ensure that file change tracker is in the tracking mode before scanning the journal.
                    var scanJournalResult = ScanChangeJournal(
                        loggingContext,
                        fileChangeTracker,
                        graphInputArtifactChanges,
                        fileContentTable,
                        timeLimitForJournalScanning);

                    if (scanJournalResult.Succeeded)
                    {
                        if (graphInputArtifactChanges != null && graphInputArtifactChanges.HaveNoChanges)
                        {
                            result.Matches = true;
                            result.FilesChecked = 0; // Hurray!
                            result.MissType = GraphCacheMissReason.NoMiss;

                            shouldMatchInputFiles = false;
                        }
                    }
                }
            }
            else
            {
                // Loading file changed tracker failed, or the loaded file change tracker is not in tracking mode, i.e.,
                // the file change tracker can be in disabled mode due to failure in tracking files in previous build.
                fileChangeTracker = FileChangeTracker.StartTrackingChanges(
                    loggingContext,
                    journalState.VolumeMap,
                    journalState.Journal,
                    graphFingerprint.ExactFingerprint.BuildEngineHash.ToString(),
                    atomicSaveToken); // Passing the save token ensure that the file change tracker is owned by (or correlated to) the input tracker.
            }

            if (shouldMatchInputFiles)
            {
                // We reach this point if
                // - loading file change tracking set failed, or
                // - the loaded file change tracker is not in tracking mode, or
                // - the loaded file change tracker has different atomic save token from the one in the input tracker, or
                // - journal scanning detects file changes.
                // For the last case, we ensure that those file definitely changed by checking their content hashes against
                // those recorded by the input tracker.
                result = MatchesInputFiles(
                        loggingContext,
                        reader,
                        fileChangeTracker,
                        fileContentTable,
                        matchingFingerprint.Value,
                        maxDegreeOfParallelism,
                        configuration.Logging,
                        checkAllPossiblyChangedPaths,
                        graphInputArtifactChanges?.PossiblyChangedPaths,
                        graphInputArtifactChanges?.ChangedDirs);
            }

            // If we have a positive match, i.e., no file changes nor directory membership changes are detected, then either we finished a journal scan 
            // without detecting any changes or we re-visited/re-hashed all needed files with CheckInputFileForChanges and re-built the change tracking set.
            // In either case, the change tracking set has data valuable for the next scan, and so we should persist it.
            if (result.Matches)
            {
                FileEnvelopeId savedFileEnvelopeId = fileChangeTracker.GetFileEnvelopeToSaveWith(overrideFileEnvelopeId: atomicSaveToken);
                // To keep the input tracker and the file change tracker in sync, we save the file change tracker with the atomic
                // token saved in the input tracker.
                fileChangeTracker.SaveTrackingStateIfChanged(
                    changeTrackingStatePath, 
                    savedFileEnvelopeId);
            }

            return result;
        }

        private static ScanningJournalResult ScanChangeJournal(
            LoggingContext loggingContext,
            FileChangeTracker fileChangeTracker,
            GraphInputArtifactChanges graphInputArtifactChanges,
            FileContentTable fileContentTable,
            TimeSpan? timeLimit)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(fileChangeTracker != null);
            Contract.Requires(fileChangeTracker.IsTrackingChanges);
            Contract.Requires(fileContentTable != null);

            var fileChangeProcessor = new FileChangeProcessor(loggingContext, fileChangeTracker);

            if (graphInputArtifactChanges != null)
            {
                fileChangeProcessor.Subscribe(graphInputArtifactChanges);
            }

            fileChangeProcessor.Subscribe(fileContentTable);

            return fileChangeProcessor.TryProcessChanges(
                timeLimit,
                Logger.Log.JournalProcessingStatisticsForGraphReuseCheck,
                Logger.Log.JournalProcessingStatisticsForGraphReuseCheckTelemetry);
        }

        private static MatchResult MatchesInputFiles(
            LoggingContext loggingContext,
            BinaryReader reader,
            FileChangeTracker tracker,
            FileContentTable fileContentTable,
            ContentFingerprint matchingFingerprint,
            int maxDegreeOfParallelism,
            ILoggingConfiguration loggingConfig,
            bool checkAllPossiblyChangedPaths,
            HashSet<string> possiblyChangedPaths = null,
            HashSet<string> possiblyChangedDirs = null)
        {
            Logger.Log.VisitingSpecFilesStart(loggingContext);
            Stopwatch stopwatch = Stopwatch.StartNew();

            // Read in the files from the previous run
            int fileCount = reader.ReadInt32();

            // The change journal either detected that there may have been changed files, or the journal was disabled. In
            // either case we must check files one-by-one ensuring that their current hash matches that of the previous run.

            // definitelyChangedPaths holds paths that were changed. If journal scanning is enabled, possiblyChangedPaths
            // may contain false positives that need to be verified by looking at the files. If the journal is disabled,
            // possiblyChanged paths will be empty, even if there are paths that have changed. This collection is the source of truth
            ConcurrentQueue<string> definitelyChangedPaths = new ConcurrentQueue<string>();

            // upToDatePaths will hold paths that are tracked by the FileChangeTracker and have a known hash. These files need
            // not be tracked in the future as long as the same FileChangeTracker instance is used
            ConcurrentDictionary<string, ContentHash> upToDatePaths = new ConcurrentDictionary<string, ContentHash>(StringComparer.OrdinalIgnoreCase);

            int reportedCount = 0;
            BlockingCollection<(string, ContentHash)> filesToCheck = new BlockingCollection<(string, ContentHash)>();
            Thread[] threads = new Thread[maxDegreeOfParallelism];
            for (int i = 0; i < maxDegreeOfParallelism; i++)
            {
                threads[i] = new Thread(() =>
                {
                    while (!filesToCheck.IsCompleted)
                    {
                        (string, ContentHash) fileToCheck;
                        if (filesToCheck.TryTake(out fileToCheck, Timeout.Infinite))
                        {
                            // unless explicitly requested to check them all, don't bother checking any more hashes after we get our first failure.
                            if (definitelyChangedPaths.Count == 0 || checkAllPossiblyChangedPaths)
                            {
                                CheckInputFileForChanges(loggingContext, fileContentTable, tracker, fileToCheck, definitelyChangedPaths, upToDatePaths, ref reportedCount);
                            }
                            else
                            {
                                return;
                            }
                        }
                    }
                });
            }

            for (int i = 0; i < fileCount; i++)
            {
                string path = reader.ReadString();
                ContentHash hash = ContentHashingUtilities.CreateFrom(reader);

                // The change journal based change detection may have false positives. So when it is able to
                // detect changes we still need to double check by hashing the file to make sure it really changed.
                // That may happen due to metadata changes to the files.
                // Getting to this point with null possiblyChangedPaths either means that change journal based
                // detection either wasn't available or failed. In that case all files must be checked.
                if (!tracker.IsTrackingChanges || possiblyChangedPaths == null || possiblyChangedPaths.Contains(path))
                {
                    filesToCheck.Add((path, hash));
                }
                else
                {
                    upToDatePaths.Add(path, hash);
                }
            }

            var numFilesToCheck = filesToCheck.Count;

            for (int i = 0; i < maxDegreeOfParallelism; i++)
            {
                threads[i].Start();
            }

            filesToCheck.CompleteAdding();

            // This can take a while if we're on a cold disk cache. We'd better start up a status timer
            using (new Timer(
                o =>
                {
                    Logger.Log.CheckingForPipGraphReuseStatus(loggingContext, numFilesToCheck - filesToCheck.Count, filesToCheck.Count);
                },
                null,
                BuildXLEngine.GetTimerUpdatePeriodInMs(loggingConfig),
                BuildXLEngine.GetTimerUpdatePeriodInMs(loggingConfig)))
            {
                foreach (var t in threads)
                {
                    t.Join();
                }
            }

            bool failed = definitelyChangedPaths.Count > 0;
            string firstMiss = definitelyChangedPaths.FirstOrDefault();
            GraphCacheMissReason missReason = failed ? GraphCacheMissReason.SpecFileChanges : GraphCacheMissReason.NoMiss;

            // All of the files were the same. Now double check the identities of all of the directories
            // Read in the directories from the previous run
            if (!failed)
            {
                int directoryCount = reader.ReadInt32();
                for (int i = 0; i < directoryCount; i++)
                {
                    string directoryPath = reader.ReadString();
                    ContentHash hash = ContentHashingUtilities.CreateFrom(reader);
                    bool shouldCheckFingerprint = possiblyChangedDirs == null || possiblyChangedDirs.Contains(directoryPath);

                    if (shouldCheckFingerprint)
                    {
                        var possibleDirectoryFingerprint = TryComputeDirectoryMembershipFingerprint(tracker, directoryPath);
                        if (!possibleDirectoryFingerprint.Succeeded)
                        {
                            failed = true;
                            firstMiss = directoryPath;
                            missReason = GraphCacheMissReason.NotAllDirectoryEnumerationsAreAccounted;
                            Logger.Log.InputTrackerUnableToDetectChangeInEnumeratedDirectory(
                                loggingContext, 
                                directoryPath, 
                                possibleDirectoryFingerprint.Failure.DescribeIncludingInnerFailures());
                            break;
                        }

                        var directoryFingerprint = possibleDirectoryFingerprint.Result;

                        if (!directoryFingerprint.Hash.Equals(hash))
                        {
                            // Miss
                            failed = true;
                            firstMiss = directoryPath;
                            missReason = GraphCacheMissReason.DirectoryChanged;
                            Logger.Log.InputTrackerDetectedChangeInEnumeratedDirectory(
                                loggingContext, 
                                directoryPath, 
                                hash.ToString(), 
                                directoryFingerprint.Hash.ToString());
                            break;
                        }
                    }
                }
            }

            stopwatch.Stop();
            Logger.Log.VisitingSpecFilesComplete(loggingContext, (int)stopwatch.ElapsedMilliseconds, numFilesToCheck);

            if (checkAllPossiblyChangedPaths)
            {
                Contract.Assert(fileCount == upToDatePaths.Count + definitelyChangedPaths.Count);
            }

            return new MatchResult
            {
                Matches = !failed,
                FirstMissIdentifier = firstMiss,
                MissType = missReason,
                MatchingFingerprint = matchingFingerprint,
                FilesChecked = numFilesToCheck,
                InputChanges = new InputChanges
                {
                    UnchangedPaths = upToDatePaths,
                    ChangedPaths = definitelyChangedPaths,
                    FileChangeTracker = tracker,
                    IsChangedPathSetComplete = checkAllPossiblyChangedPaths,
                },
            };
        }

        /// <summary>
        /// Checks whether the content of an input file matches a previously computed hash.
        /// </summary>
        private static void CheckInputFileForChanges(
            LoggingContext loggingContext,
            FileContentTable fileContentTable,
            FileChangeTracker tracker,
            (string, ContentHash) fileToCheck,
            ConcurrentQueue<string> changedPaths,
            ConcurrentDictionary<string, ContentHash> upToDatePaths,
            ref int reportedCount)
        {
            bool mismatched = false;
            string path = fileToCheck.Item1;

            try
            {
                if (!File.Exists(path))
                {
                    // Avoid try-catch by checking file existence.
                    if (fileToCheck.Item2 != WellKnownContentHashes.AbsentFile)
                    {
                        mismatched = true;

                        if (Interlocked.Increment(ref reportedCount) <= MaxReportedChangedInputFiles)
                        {
                            Logger.Log.InputTrackerDetectedChangedInputFileByCheckingContentHash(
                                loggingContext,
                                path,
                                WellKnownContentHashes.AbsentFile.ToString(), 
                                fileToCheck.Item2.ToString());
                        }
                    }
                }
                // If the path exists, then we only need to hash it and compare the result if the file was actually read
                // In the case of a probe-only access that resulted in the path being present, the fact that the path is 
                // present is enough
                else if (fileToCheck.Item2 != WellKnownContentHashes.ExistentFileProbe)
                {
                    VersionedFileIdentityAndContentInfo identityAndContentInfo =
                        fileContentTable.GetAndRecordContentHashAsync(
                            path,
                            beforeClose: (handle, result) =>
                                         {
                                         // Since the file potentially changed, we need to track it again.
                                         Analysis.IgnoreResult(
                                                 tracker.TryTrackChangesToFile(
                                                     handle,
                                                     path,
                                                     result.VersionedFileIdentityAndContentInfo.Identity));
                                         }).GetAwaiter().GetResult().VersionedFileIdentityAndContentInfo;

                    if (identityAndContentInfo.FileContentInfo.Hash != fileToCheck.Item2)
                    {
                        mismatched = true;

                        if (Interlocked.Increment(ref reportedCount) <= MaxReportedChangedInputFiles)
                        {
                            Logger.Log.InputTrackerDetectedChangedInputFileByCheckingContentHash(
                                loggingContext,
                                path,
                                identityAndContentInfo.FileContentInfo.Hash.ToString(), 
                                fileToCheck.Item2.ToString());
                        }
                    }
                }
            }
            catch (BuildXLException ex)
            {
                mismatched = true;

                if (Interlocked.Increment(ref reportedCount) <= MaxReportedChangedInputFiles)
                {
                    Logger.Log.InputTrackerUnableToDetectChangedInputFileByCheckingContentHash(
                        loggingContext,
                        path,
                        fileToCheck.Item2.ToString(),
                        ex.LogEventMessage);
                }
            }

            if (mismatched)
            {
                changedPaths.Enqueue(fileToCheck.Item1);
            }
            else
            {
                upToDatePaths.Add(fileToCheck.Item1, fileToCheck.Item2);
            }
        }

        /// <summary>
        /// Normalizes environment variable value.
        /// </summary>
        /// <remarks>
        /// Normalizing environment variable value means replacing the value with <see cref="UnsetVariableMarker"/> if the value is <code>null</code>.
        /// </remarks>
        public static string NormalizeEnvironmentVariableValue(string value)
        {
            return value ?? UnsetVariableMarker;
        }

        /// <summary>
        /// Result of checking to see if the input tracker matches the previous run
        /// </summary>
        public struct MatchResult
        {
            /// <summary>
            /// True if it is a match
            /// </summary>
            public bool Matches;

            /// <summary>
            /// Number of input files checked for matching hashes
            /// </summary>
            public int FilesChecked;

            /// <summary>
            /// The fingerprint that matched, if the result is a match
            /// </summary>
            public ContentFingerprint MatchingFingerprint;

            /// <summary>
            /// What caused the miss.
            /// </summary>
            public GraphCacheMissReason MissType;

            /// <summary>
            /// Textual description of the first miss
            /// </summary>
            public string FirstMissIdentifier;

            /// <summary>
            /// Inputs not changed
            /// </summary>
            public InputChanges InputChanges;
        }

        /// <summary>
        /// Information about which inputs were not changed
        /// </summary>
        public sealed class InputChanges
        {
            /// <summary>
            /// Paths and hashes of files that did not change from the previous run. These files are guaranteed
            /// to already have been tracked with the associated FileChangeTracker.
            /// </summary>
            [CanBeNull]
            public ConcurrentDictionary<string, ContentHash> UnchangedPaths;

            /// <summary>
            /// Paths of files that definitely changed since the previous run, or <code>null</code>
            /// if no such information is available.
            /// </summary>
            [CanBeNull]
            public ConcurrentQueue<string> ChangedPaths;

            /// <summary>
            /// A file change tracker that may have been created when checking for matches
            /// </summary>
            public FileChangeTracker FileChangeTracker;

            /// <summary>
            /// Whether the <see cref="ChangedPaths"/> set is complete, i.e., whether it contains
            /// all paths that actually changed or just a subset.
            /// </summary>
            public bool IsChangedPathSetComplete;
        }
    }
}
