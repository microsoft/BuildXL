// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Computes fingerprints of given pips. 
    /// </summary>
    /// <remarks>
    /// This class is thread safe.
    /// For legacy reason, the computed fingerprint is of type <see cref="ContentFingerprint"/>.
    /// Pip fingerprints have the following form (in EBNF)
    /// 
    ///     FP ::= { List(NV) }              /* Fingerprint */
    ///     NV ::= name : V                  /* Named value */
    ///     SV ::= Path                      /* Scalar value */
    ///            | String 
    ///            | Int
    ///            | Hash
    ///     V  ::= SV
    ///            | FP                      /* Nested value */
    ///            | [ List(SV | NV) ]       /* Collection */
    /// 
    /// The above EBNF is enforced by the <see cref="IFingerprinter"/> and <see cref="ICollectionFingerprinter"/> interfaces.
    /// Note that if one translates FP into JSON format, then the resulting JSON may be ill-formed because of the collection rule.
    /// To handle that, a JSON fingerprinter should massage NV in the collection rule into { NV }.
    /// </remarks>
    public abstract class PipFingerprinter
    {
        /// <summary>
        /// Name of the current output content hashing algorithm.
        /// </summary>
        private static readonly string s_outputContentHashAlgorithmName = ContentHashingUtilities.HashInfo.Name;

        /// <summary>
        /// Refers to a function which maps file artifacts to pip data content. This is used inject the normalized pip data into the fingerprint
        /// rather than the content hash of the file which represents machine dependent content because of the full paths in the output file.
        /// </summary>
        public delegate PipData PipDataLookup(FileArtifact artifact);

        private readonly PathTable m_pathTable;
        private readonly PipFragmentRenderer.ContentHashLookup m_contentHashLookup;
        private readonly PipDataLookup m_pipDataLookup;
        private ExtraFingerprintSalts m_extraFingerprintSalts;
        private readonly ExpandedPathFileArtifactComparer m_expandedPathFileArtifactComparer;
        private readonly Comparer<DirectoryArtifact> m_directoryComparer;
        private readonly Comparer<FileArtifactWithAttributes> m_expandedPathFileArtifactWithAttributesComparer;

        /// <summary>
        /// The tokenizer used to handle path roots
        /// </summary>
        public readonly PathExpander PathExpander;
        
        /// <summary>
        /// Gets or sets whether fingerprint text is returned when computing fingerprints.
        /// </summary>
        public bool FingerprintTextEnabled { get; set; }

        /// <summary>
        /// The extra, optional fingerprint salt.
        /// Set by the distributed build system later when the fingerprinter was already created.
        /// </summary>
        public string FingerprintSalt
        {
            get => m_extraFingerprintSalts.FingerprintSalt;
            set => m_extraFingerprintSalts.FingerprintSalt = value;
        }

        /// <summary>
        /// The extra, optional fingerprint salt text
        /// </summary>
        public string CalculatedFingerprintSaltText => m_extraFingerprintSalts.CalculatedSaltsFingerprintText;

        /// <summary>
        /// Creates a content fingerprinter which will look up the content of pip dependencies
        /// (when fingerprinting that pip) via the given callback. The new instance is thread-safe
        /// and so note that the callback may be called concurrently.
        /// </summary>
        protected PipFingerprinter(
            PathTable pathTable,
            PipFragmentRenderer.ContentHashLookup contentHashLookup = null,
            ExtraFingerprintSalts? extraFingerprintSalts = null,
            PathExpander pathExpander = null,
            PipDataLookup pipDataLookup = null)
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            m_contentHashLookup = contentHashLookup ?? new PipFragmentRenderer.ContentHashLookup(file => FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.ZeroHash));
            m_extraFingerprintSalts = extraFingerprintSalts ?? ExtraFingerprintSalts.Default();
            m_pipDataLookup = pipDataLookup ?? new PipDataLookup(file => PipData.Invalid);
            PathExpander = pathExpander ?? PathExpander.Default;
            m_expandedPathFileArtifactComparer = new ExpandedPathFileArtifactComparer(m_pathTable.ExpandedPathComparer, pathOnly: false);
            m_directoryComparer = Comparer<DirectoryArtifact>.Create((d1, d2) => m_pathTable.ExpandedPathComparer.Compare(d1.Path, d2.Path));
            m_expandedPathFileArtifactWithAttributesComparer = Comparer<FileArtifactWithAttributes>.Create((f1, f2) => m_pathTable.ExpandedPathComparer.Compare(f1.Path, f2.Path));
        }

        /// <summary>
        /// Returns the content hash lookup function used by this fingerprinter.
        /// </summary>
        public PipFragmentRenderer.ContentHashLookup ContentHashLookupFunction => (file) => m_contentHashLookup(file);

        /// <summary>
        /// Computes the weak fingerprint of a pip. This accounts for all statically declared inputs including
        /// unsafe config option. This does not account for dynamically discovered input assertions.
        /// </summary>
        public ContentFingerprint ComputeWeakFingerprint(Pip pip) => ComputeWeakFingerprint(pip, out string dummyInputText);

        /// <summary>
        /// Computes the weak fingerprint of a pip. This accounts for all statically declared inputs including
        /// unsafe config option. This does not account for dynamically discovered input assertions.
        /// </summary>
        public ContentFingerprint ComputeWeakFingerprint(Pip pip, out string fingerprintInputText)
        {
            Contract.Requires(pip != null);

            // This used to check process.ProducedPathIndependentOutput to determine whether to use semantic paths or
            // not. That was when the MountPathExpander tokenized paths based on what mount they were under. It now only
            // has this behavior for the user profile directory which is much more scoped, hence it is enabled by default.
            using (HashingHelper hashingHelper = CreateHashingHelper(useSemanticPaths: true))
            {
                AddWeakFingerprint(hashingHelper, pip);

                // Bug #681083 include somehow information about process.ShutdownProcessPipId and process.ServicePipDependencies
                //               but make sure it doesn't depend on PipIds (because they are not stable between builds)
                fingerprintInputText = FingerprintTextEnabled 
                    ? (m_extraFingerprintSalts.CalculatedSaltsFingerprintText + Environment.NewLine + hashingHelper.FingerprintInputText) 
                    : string.Empty;

                return new ContentFingerprint(hashingHelper.GenerateHash());
            }
        }

        /// <summary>
        /// Adds the weak fingerprint of a pip into the fingerprinter.
        /// </summary>
        public void AddWeakFingerprint(IFingerprinter fingerprinter, Pip pip)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(pip != null);

            fingerprinter.Add("ExecutionAndFingerprintOptionsHash", m_extraFingerprintSalts.CalculatedSaltsFingerprint);

            // Fingerprints must change when outputs are hashed with a different algorithm.
            fingerprinter.Add("ContentHashAlgorithmName", s_outputContentHashAlgorithmName);

            fingerprinter.Add("PipType", GetHashMarker(pip));

            switch (pip.PipType)
            {
                case PipType.Process:
                    AddWeakFingerprint(fingerprinter, pip as Process);
                    break;
                case PipType.WriteFile:
                    AddWeakFingerprint(fingerprinter, pip as WriteFile);
                    break;
                case PipType.CopyFile:
                    AddWeakFingerprint(fingerprinter, pip as CopyFile);
                    break;
                case PipType.HashSourceFile:
                    AddWeakFingerprint(fingerprinter, pip as HashSourceFile);
                    break;
                case PipType.SealDirectory:
                    AddWeakFingerprint(fingerprinter, pip as SealDirectory);
                    break;
                default:
                    AssertFailureNotSupportedPip(pip);
                    break;
            }
        }

        /// <summary>
        /// Adds the members of <see cref="HashSourceFile"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, HashSourceFile hashSourceFile)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(hashSourceFile != null);

            fingerprinter.Add("File", hashSourceFile.Artifact);
        }

        /// <summary>
        /// Adds the members of <see cref="CopyFile"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, CopyFile copyFile)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(copyFile != null);

            AddFileDependency(fingerprinter, "Source", copyFile.Source);
            AddFileOutput(fingerprinter, "Destination", copyFile.Destination);
        }

        /// <summary>
        /// Adds the members of <see cref="WriteFile"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, WriteFile writeFile)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(writeFile != null);

            AddFileOutput(fingerprinter, "Destination", writeFile.Destination);
            AddPipData(fingerprinter, "Contents", writeFile.Contents);
            fingerprinter.Add("Encoding", (byte)writeFile.Encoding);
        }

        /// <summary>
        /// Adds the members of <see cref="SealDirectory"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, SealDirectory sealDirectory)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(sealDirectory != null);

            fingerprinter.Add("Path", sealDirectory.DirectoryRoot);
            fingerprinter.Add("Kind", sealDirectory.Kind.ToString());
            fingerprinter.Add("Scrub", sealDirectory.Scrub.ToString());

            // Sort the contents based on their members' expanded paths so that they are stable across different path tables.
            var sortedContents = SortedReadOnlyArray<FileArtifact, ExpandedPathFileArtifactComparer>.CloneAndSort(sealDirectory.Contents, m_expandedPathFileArtifactComparer);

            fingerprinter.AddCollection<FileArtifact, ReadOnlyArray<FileArtifact>>("Contents", sortedContents, (fp, f) => AddFileDependency(fp, f));
            fingerprinter.AddCollection<StringId, ReadOnlyArray<StringId>>("Patterns", sealDirectory.Patterns, (fp, p) => fp.Add(p));
            fingerprinter.Add("IsComposite", sealDirectory.IsComposite.ToString());
            fingerprinter.AddCollection<DirectoryArtifact, IReadOnlyList<DirectoryArtifact>>("ComposedDirectories", sealDirectory.ComposedDirectories, (fp, d) => AddDirectoryDependency(fp, d));
        }

        /// <summary>
        /// Adds the members of <see cref="Process"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, Process process)
        {
            AddFileDependency(fingerprinter, "Executable", process.Executable);
            fingerprinter.Add("WorkingDirectory", process.WorkingDirectory);

            if (process.StandardInput.IsData)
            {
                // We only add standard input if it is data. If it is a file, then it is guaranteed to be in the dependency list.
                AddPipData(fingerprinter, "StandardInputData", process.StandardInput.Data);
            }

            AddFileOutput(fingerprinter, "StandardError", process.StandardError);
            AddFileOutput(fingerprinter, "StandardOutput", process.StandardOutput);

            fingerprinter.AddOrderIndependentCollection<FileArtifact, ReadOnlyArray<FileArtifact>>("Dependencies", process.Dependencies, (fp, f) => AddFileDependency(fp, f), m_expandedPathFileArtifactComparer);            
            fingerprinter.AddOrderIndependentCollection<DirectoryArtifact, ReadOnlyArray<DirectoryArtifact>>("DirectoryDependencies", process.DirectoryDependencies, (fp, d) => AddDirectoryDependency(fp, d), m_directoryComparer);
                        
            fingerprinter.AddOrderIndependentCollection<FileArtifactWithAttributes, ReadOnlyArray<FileArtifactWithAttributes>>("Outputs", process.FileOutputs, (fp, f) => AddFileOutput(fp, f), m_expandedPathFileArtifactWithAttributesComparer);
            fingerprinter.AddOrderIndependentCollection<DirectoryArtifact, ReadOnlyArray<DirectoryArtifact>>("DirectoryOutputs", process.DirectoryOutputs, (h, p) => h.Add(p.Path), m_directoryComparer);

            fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>("UntrackedPaths", process.UntrackedPaths, (h, p) => h.Add(p), m_pathTable.ExpandedPathComparer);
            fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>("UntrackedScopes", process.UntrackedScopes, (h, p) => h.Add(p), m_pathTable.ExpandedPathComparer);

            fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>("PreserveOutputWhitelist", process.PreserveOutputWhitelist, (h, p) => h.Add(p), m_pathTable.ExpandedPathComparer);

            fingerprinter.Add("HasUntrackedChildProcesses", process.HasUntrackedChildProcesses ? 1 : 0);
            fingerprinter.Add("AllowUndeclaredSourceReads", process.AllowUndeclaredSourceReads ? 1 : 0);
            fingerprinter.Add("AbsentPathProbeUnderOpaquesMode", (byte)process.ProcessAbsentPathProbeInUndeclaredOpaquesMode);

            // When DisableCacheLookup is set, the pip is marked as perpetually dirty for incremental scheduling.
            // It must also go to the weak fingerprint so IS will get a miss when you change from the DisableCacheLookup = false
            // to DisableCacheLookup = true.
            if (process.DisableCacheLookup)
            {
                fingerprinter.Add("DisableCacheLookup", ContentHashingUtilities.CreateRandom());
            }

            fingerprinter.Add("DoubleWritePolicy", (byte)process.DoubleWritePolicy);

            if (process.RequiresAdmin)
            {
                fingerprinter.Add("RequiresAdmin", 1);
            }
            
            fingerprinter.Add("NeedsToRunInContainer", process.NeedsToRunInContainer ? 1 : 0);
            fingerprinter.Add("ContainerIsolationLevel", (byte) process.ContainerIsolationLevel);

            AddPipData(fingerprinter, "Arguments", process.Arguments);
            if (process.ResponseFileData.IsValid)
            {
                AddPipData(fingerprinter, "ResponseFileData", process.ResponseFileData);
            }

            fingerprinter.AddCollection<EnvironmentVariable, ReadOnlyArray<EnvironmentVariable>>(
                "EnvironmentVariables",
                process.EnvironmentVariables,
                (fCollection, env) =>
                {
                    if (env.IsPassThrough)
                    {
                        fCollection.Add(env.Name.ToString(m_pathTable.StringTable), "Pass-through");
                    }
                    else
                    {
                        AddPipData(fCollection, env.Name.ToString(m_pathTable.StringTable), env.Value);
                    }
                });

            fingerprinter.Add("WarningTimeout", process.WarningTimeout.HasValue ? process.WarningTimeout.Value.Ticks : -1);
            fingerprinter.Add("Timeout", process.Timeout.HasValue ? process.Timeout.Value.Ticks : -1);

            if (process.WarningRegex.IsValid)
            {
                fingerprinter.Add("WarningRegex.Pattern", process.WarningRegex.Pattern);
                fingerprinter.Add("WarningRegex.Options", (int)process.WarningRegex.Options);
            }

            if (process.ErrorRegex.IsValid)
            {
                fingerprinter.Add("ErrorRegex.Pattern", process.ErrorRegex.Pattern);
                fingerprinter.Add("ErrorRegex.Options", (int)process.ErrorRegex.Options);
            }

            fingerprinter.AddCollection<int, ReadOnlyArray<int>>("SuccessExitCodes", process.SuccessExitCodes, (h, i) => h.Add(i));
        }

        /// <summary>
        /// Adds pip data (such as a command line or the contents of a response file) to a fingerprint stream.
        /// </summary>
        private void AddPipData(IFingerprinter fingerprinter, string name, PipData data)
        {
            Contract.Requires(data.IsValid);
            Contract.Requires(name != null);
            Contract.Requires(fingerprinter != null);

            fingerprinter.Add(name, data.ToString(path => PathExpander.ExpandPath(m_pathTable, path).ToUpperInvariant(), m_pathTable.StringTable));
        }

        /// <summary>
        /// Adds a file artifact and its content hash to the fingerprint stream.
        /// When fingerprinting pips, this should be used to account for file artifact dependencies.
        /// </summary>
        protected virtual void AddFileDependency(ICollectionFingerprinter fingerprinter, FileArtifact fileArtifact)
        {
            Contract.Requires(fingerprinter != null);

            if (fileArtifact.IsValid)
            {
                PipData filePipData;
                if ((filePipData = m_pipDataLookup(fileArtifact)).IsValid)
                {
                    // Response files (and other tool configuration files) contain the fully expanded paths in the command-line arguments, but
                    // we need to fingerprint on something more abstract (e.g. normalized paths).
                    // We used to support the notion of path independent weak fingerprints (i.e. roots like D:\src\BuildXL would be tokenized as %SourceRoot%). 
                    // However, the actual write file output would not have these tokens since they need to be read by tools. To address that, we traversed
                    // into write file outputs so we could get their "path normalized" content(ie with paths tokenized).
                    fingerprinter.AddNested(
                        fileArtifact.Path,
                        fp => AddPipData(fp, "PathNormalizedWriteFileContent", filePipData));
                }
                else
                {
                    fingerprinter.Add(fileArtifact.Path, m_contentHashLookup(fileArtifact).Hash);
                }
            }
            else
            {
                fingerprinter.Add(fileArtifact.Path);
            }
        }

        /// <summary>
        /// Adds a file artifact and its content hash to the fingerprint stream.
        /// When fingerprinting pips, this should be used to account for file artifact dependencies.
        /// </summary>
        protected virtual void AddFileDependency(IFingerprinter fingerprinter, string name, FileArtifact fileArtifact)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(name != null);

            if (fileArtifact.IsValid)
            {
                fingerprinter.Add(name, fileArtifact.Path, m_contentHashLookup(fileArtifact).Hash);
            }
            else
            {
                fingerprinter.Add(name, fileArtifact.Path);
            }
        }

        /// <summary>
        /// Adds a directory artifact and its content hash to the fingerprint stream.
        /// When fingerprinting pips, this should be used to account for directory artifact dependencies.
        /// </summary>
        protected virtual void AddDirectoryDependency(ICollectionFingerprinter fingerprinter, DirectoryArtifact directoryArtifact)
        {
            Contract.Requires(fingerprinter != null);

            fingerprinter.Add(directoryArtifact.Path);
        }

        /// <summary>
        /// Adds a file artifact with attributes to the fingerprint stream.
        /// </summary>
        protected virtual void AddFileOutput(ICollectionFingerprinter fingerprinter, FileArtifactWithAttributes fileArtifact)
        {
            Contract.Requires(fingerprinter != null);

            // For attributed file artifacts both path and attributes are critical for fingerprinting
            fingerprinter.AddNested(fileArtifact.Path, fp => fp.Add("Attributes", (int)fileArtifact.FileExistence));
        }

        /// <summary>
        /// Adds a file artifact (but no known content hash) to the fingerprint stream.
        /// When fingerprinting pips, this should be used to account for file artifact that are written but not read.
        /// </summary>
        protected virtual void AddFileOutput(IFingerprinter fingerprinter, string name, FileArtifact fileArtifact)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(name != null);

            fingerprinter.Add(name, fileArtifact.Path);
        }

        private HashingHelper CreateHashingHelper(bool useSemanticPaths, HashAlgorithmType hashAlgorithmType = HashAlgorithmType.SHA1Managed)
        {
            return new HashingHelper(
                m_pathTable,
                recordFingerprintString: FingerprintTextEnabled,
                pathExpander: useSemanticPaths ? PathExpander : PathExpander.Default,
                hashAlgorithmType: hashAlgorithmType);
        }

        private string GetHashMarker(Pip pip)
        {
            switch (pip.PipType)
            {
                case PipType.Process:
                    return "Process";
                case PipType.WriteFile:
                    return "WriteFile";
                case PipType.CopyFile:
                    return "CopyFile";
                case PipType.HashSourceFile:
                    return "HashSourceFile";
                case PipType.SealDirectory:
                    return "SealDirectory";
            }

            AssertFailureNotSupportedPip(pip);
            return null;
        }

        private void AssertFailureNotSupportedPip(Pip pip) => throw Contract.AssertFailure(I($"{nameof(PipFingerprinter)} does not support pip type '{pip.PipType.ToString()}'"));
    }
}
