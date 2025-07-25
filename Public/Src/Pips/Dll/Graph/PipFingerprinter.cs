// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Pips.Graph
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

        /// <summary>
        /// Refers to a function which maps a process to its source-change-affected inputs. 
        /// </summary>
        public delegate IReadOnlyList<AbsolutePath> SourceChangeAffectedInputsLookup(Process process);

        /// <summary>
        /// Delegate for getting the fingerpring salt for a particular process.
        /// </summary>
        public delegate string PipFingerprintSaltLookup(Process process);

        private readonly PathTable m_pathTable;
        private readonly PipFragmentRenderer.ContentHashLookup m_contentHashLookup;
        private readonly PipDataLookup m_pipDataLookup;
        private readonly SourceChangeAffectedInputsLookup m_sourceChangeAffectedInputsLookup;
        private readonly ExpandedPathFileArtifactComparer m_expandedPathFileArtifactComparer;
        private readonly Comparer<FileArtifactWithAttributes> m_expandedPathFileArtifactWithAttributesComparer;
        private readonly Comparer<EnvironmentVariable> m_environmentVariableComparer;
        private readonly PipFragmentRenderer m_pipFragmentRenderer;
        private readonly RegexDescriptorComparer m_regexDescriptorComparer;
        private readonly BreakawayChildProcessComparer m_breakawayChildProcessComparer;

        /// <summary>
        /// Directory comparer.
        /// </summary>
        protected readonly Comparer<DirectoryArtifact> DirectoryComparer;

        /// <summary>
        /// The tokenizer used to handle path roots
        /// </summary>
        public readonly PathExpander PathExpander;

        /// <summary>
        /// Gets or sets whether fingerprint text is returned when computing fingerprints.
        /// </summary>
        public bool FingerprintTextEnabled { get; set; }

        /// <summary>
        /// Extra fingerprint salts.
        /// </summary>
        protected ExtraFingerprintSalts ExtraFingerprintSalts;

        /// <summary>
        /// The extra, optional fingerprint salt.
        /// Set by the distributed build system later when the fingerprinter was already created.
        /// </summary>
        public string FingerprintSalt
        {
            get => ExtraFingerprintSalts.FingerprintSalt;
            set => ExtraFingerprintSalts.FingerprintSalt = value;
        }

        private readonly PipFingerprintSaltLookup m_pipFingerprintSaltLookup;

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
            PipDataLookup pipDataLookup = null,
            SourceChangeAffectedInputsLookup sourceChangeAffectedInputsLookup = null,
            PipFingerprintSaltLookup pipFingerprintSaltLookup = null)
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            m_contentHashLookup = contentHashLookup ?? new PipFragmentRenderer.ContentHashLookup(file => FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.ZeroHash));
            ExtraFingerprintSalts = extraFingerprintSalts ?? ExtraFingerprintSalts.Default();
            m_pipDataLookup = pipDataLookup ?? new PipDataLookup(file => PipData.Invalid);
            PathExpander = pathExpander ?? PathExpander.Default;
            m_expandedPathFileArtifactComparer = new ExpandedPathFileArtifactComparer(m_pathTable.ExpandedPathComparer, pathOnly: false);
            DirectoryComparer = Comparer<DirectoryArtifact>.Create((d1, d2) => m_pathTable.ExpandedPathComparer.Compare(d1.Path, d2.Path));
            m_environmentVariableComparer = Comparer<EnvironmentVariable>.Create((ev1, ev2) => { return ev1.Name.ToString(pathTable.StringTable).CompareTo(ev2.Name.ToString(pathTable.StringTable)); });
            m_expandedPathFileArtifactWithAttributesComparer = Comparer<FileArtifactWithAttributes>.Create((f1, f2) => m_pathTable.ExpandedPathComparer.Compare(f1.Path, f2.Path));
            m_sourceChangeAffectedInputsLookup = sourceChangeAffectedInputsLookup ?? new SourceChangeAffectedInputsLookup(process => ReadOnlyArray<AbsolutePath>.Empty);
            m_pipFragmentRenderer = new PipFragmentRenderer(
                pathExpander: path => PathExpander.ExpandPath(pathTable, path).ToCanonicalizedPath(),
                pathTable.StringTable,
                // Do not resolve monikers because their values will be different every build.
                monikerRenderer: m => m,
                // Use the hash lookup delegate that was passed as an argument.
                // PipFragmentRenderer can accept a null value here, and it has special logic for such cases.
                m_contentHashLookup);
            m_pipFingerprintSaltLookup = pipFingerprintSaltLookup ?? new PipFingerprintSaltLookup(_ => string.Empty);
            m_regexDescriptorComparer = new RegexDescriptorComparer(pathTable.StringTable.OrdinalComparer);
            m_breakawayChildProcessComparer = new BreakawayChildProcessComparer(pathTable.StringTable.OrdinalComparer);
        }

        /// <summary>
        /// Returns the content hash lookup function used by this fingerprinter.
        /// </summary>
        public PipFragmentRenderer.ContentHashLookup ContentHashLookupFunction => (file) => m_contentHashLookup(file);

        /// <summary>
        /// Computes the weak fingerprint of a pip. This accounts for all statically declared inputs.
        /// This does not account for dynamically discovered input assertions.
        /// </summary>
        public ContentFingerprint ComputeWeakFingerprint(Pip pip) => ComputeWeakFingerprint(pip, out string dummyInputText);

        /// <summary>
        /// Computes the weak fingerprint of a pip. This accounts for all statically declared inputs including
        /// unsafe config option. This does not account for dynamically discovered input assertions.
        /// </summary>
        public virtual ContentFingerprint ComputeWeakFingerprint(Pip pip, out string fingerprintInputText)
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
                    ? (ExtraFingerprintSalts.CalculatedSaltsFingerprintText + Environment.NewLine + hashingHelper.FingerprintInputText)
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

            fingerprinter.AddNested(PipFingerprintField.ExecutionAndFingerprintOptions, fp => ExtraFingerprintSalts.AddFingerprint(fp, pip.BypassFingerprintSalt));

            // Fingerprints must change when outputs are hashed with a different algorithm.
            fingerprinter.Add(PipFingerprintField.ContentHashAlgorithmName, s_outputContentHashAlgorithmName);

            fingerprinter.Add(PipFingerprintField.PipType, GetHashMarker(pip));

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
        /// Patches the given fingerprint with the given file artifacts.
        /// </summary>
        public ContentFingerprint PatchWithFileArtifactSet(ContentFingerprint fingerprint, string name, HashSet<FileArtifact> fileArtifacts)
        {
            using (var hasher = CreateHashingHelper(useSemanticPaths: true))
            {
                hasher.Add("Pip", fingerprint.Hash);
                hasher.AddOrderIndependentCollection(name, fileArtifacts, (fp, fa) => fp.Add(fa), m_expandedPathFileArtifactComparer);
                return new ContentFingerprint(hasher.GenerateHash());
            }
        }

        /// <summary>
        /// Adds the members of <see cref="HashSourceFile"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, HashSourceFile hashSourceFile)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(hashSourceFile != null);

            fingerprinter.Add(nameof(HashSourceFile.Artifact), hashSourceFile.Artifact);
        }

        /// <summary>
        /// Adds the members of <see cref="CopyFile"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, CopyFile copyFile)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(copyFile != null);

            AddFileDependency(fingerprinter, nameof(CopyFile.Source), copyFile.Source);
            AddFileOutput(fingerprinter, nameof(CopyFile.Destination), copyFile.Destination);
        }

        /// <summary>
        /// Adds the members of <see cref="WriteFile"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, WriteFile writeFile)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(writeFile != null);

            AddFileOutput(fingerprinter, nameof(WriteFile.Destination), writeFile.Destination);
            AddPipData(fingerprinter, nameof(WriteFile.Contents), writeFile.Contents);
            fingerprinter.Add(nameof(WriteFile.Encoding), (byte)writeFile.Encoding);
        }

        /// <summary>
        /// Adds the members of <see cref="SealDirectory"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, SealDirectory sealDirectory)
        {
            Contract.Requires(fingerprinter != null);
            Contract.Requires(sealDirectory != null);

            fingerprinter.Add(nameof(SealDirectory.DirectoryRoot), sealDirectory.DirectoryRoot);
            fingerprinter.Add(nameof(SealDirectory.Kind), sealDirectory.Kind.ToString());
            fingerprinter.Add(nameof(SealDirectory.Scrub), sealDirectory.Scrub.ToString());

            // Sort the contents based on their members' expanded paths so that they are stable across different path tables.
            var sortedContents = SortedReadOnlyArray<FileArtifact, ExpandedPathFileArtifactComparer>.CloneAndSort(sealDirectory.Contents, m_expandedPathFileArtifactComparer);

            fingerprinter.AddCollection<DirectoryArtifact, IReadOnlyList<DirectoryArtifact>>(nameof(SealDirectory.OutputDirectoryContents), sealDirectory.OutputDirectoryContents, (fp, d) => AddDirectoryDependency(fp, d));
            fingerprinter.AddCollection<FileArtifact, ReadOnlyArray<FileArtifact>>(nameof(SealDirectory.Contents), sortedContents, (fp, f) => AddFileDependency(fp, f));
            fingerprinter.AddCollection<StringId, ReadOnlyArray<StringId>>(nameof(SealDirectory.Patterns), sealDirectory.Patterns, (fp, p) => fp.Add(p));
            fingerprinter.Add(nameof(SealDirectory.CompositionActionKind), sealDirectory.CompositionActionKind.ToString());
            fingerprinter.Add(nameof(SealDirectory.ContentFilter), sealDirectory.ContentFilter.HasValue ? $"{sealDirectory.ContentFilter.Value.Kind} {sealDirectory.ContentFilter.Value.Regex}" : "");
            fingerprinter.AddCollection<DirectoryArtifact, IReadOnlyList<DirectoryArtifact>>(nameof(SealDirectory.ComposedDirectories), sealDirectory.ComposedDirectories, (fp, d) => AddDirectoryDependency(fp, d));
        }

        /// <summary>
        /// Adds the members of <see cref="Process"/> used in the weak fingerprint computation to the provided <see cref="IFingerprinter"/>.
        /// </summary>
        protected virtual void AddWeakFingerprint(IFingerprinter fingerprinter, Process process)
        {
            fingerprinter.Add(nameof(Process.Executable), process.Executable);
            fingerprinter.Add(nameof(Process.WorkingDirectory), process.WorkingDirectory);

            if (process.StandardInput.IsData)
            {
                // We only add standard input if it is data. If it is a file, then it is guaranteed to be in the dependency list.
                AddPipData(fingerprinter, nameof(Process.StandardInputData), process.StandardInput.Data);
            }

            AddFileOutput(fingerprinter, nameof(Process.StandardError), process.StandardError);
            AddFileOutput(fingerprinter, nameof(Process.StandardOutput), process.StandardOutput);
            AddFileOutput(fingerprinter, nameof(Process.TraceFile), process.TraceFile);

            fingerprinter.AddOrderIndependentCollection<FileArtifact, IEnumerable<FileArtifact>>(nameof(Process.Dependencies), GetRelevantProcessDependencies(process), (fp, f) => AddFileDependency(fp, f), m_expandedPathFileArtifactComparer);
            fingerprinter.AddOrderIndependentCollection<DirectoryArtifact, ReadOnlyArray<DirectoryArtifact>>(nameof(Process.DirectoryDependencies), process.DirectoryDependencies, (fp, d) => AddDirectoryDependency(fp, d), DirectoryComparer);

            fingerprinter.AddOrderIndependentCollection<FileArtifactWithAttributes, ReadOnlyArray<FileArtifactWithAttributes>>(nameof(Process.FileOutputs), process.FileOutputs, (fp, f) => AddFileOutput(fp, f), m_expandedPathFileArtifactWithAttributesComparer);
            fingerprinter.AddOrderIndependentCollection<DirectoryArtifact, ReadOnlyArray<DirectoryArtifact>>(nameof(Process.DirectoryOutputs), process.DirectoryOutputs, (h, p) => h.Add(p.Path), DirectoryComparer);

            fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>(nameof(Process.UntrackedPaths), process.UntrackedPaths, (h, p) => h.Add(p), m_pathTable.ExpandedPathComparer);
            fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>(nameof(Process.UntrackedScopes), process.UntrackedScopes, (h, p) => h.Add(p), m_pathTable.ExpandedPathComparer);

            fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>(nameof(Process.PreserveOutputAllowlist), process.PreserveOutputAllowlist, (h, p) => h.Add(p), m_pathTable.ExpandedPathComparer);

            fingerprinter.Add(nameof(Process.HasUntrackedChildProcesses), process.HasUntrackedChildProcesses ? 1 : 0);
            fingerprinter.Add(nameof(Process.AllowUndeclaredSourceReads), process.AllowUndeclaredSourceReads ? 1 : 0);
            fingerprinter.Add(nameof(Process.ProcessAbsentPathProbeInUndeclaredOpaquesMode), (byte)process.ProcessAbsentPathProbeInUndeclaredOpaquesMode);
            fingerprinter.Add(nameof(Process.TrustStaticallyDeclaredAccesses), process.TrustStaticallyDeclaredAccesses ? 1 : 0);
            fingerprinter.Add(nameof(Process.PreservePathSetCasing), process.PreservePathSetCasing ? 1 : 0);
            fingerprinter.Add(nameof(Process.WritingToStandardErrorFailsExecution), process.WritingToStandardErrorFailsExecution ? 1 : 0);
            fingerprinter.Add(nameof(Process.DisableFullReparsePointResolving), process.DisableFullReparsePointResolving ? 1 : 0);
            fingerprinter.Add(nameof(Process.RetryAttemptEnvironmentVariable), process.RetryAttemptEnvironmentVariable.IsValid ? process.RetryAttemptEnvironmentVariable.ToString(m_pathTable.StringTable) : "{Invalid}");

            // When DisableCacheLookup is set, the pip is marked as perpetually dirty for incremental scheduling.
            // It must also go to the weak fingerprint so IS will get a miss when you change from the DisableCacheLookup = false
            // to DisableCacheLookup = true.
            if (process.DisableCacheLookup)
            {
                fingerprinter.Add(nameof(Process.DisableCacheLookup), ContentHashingUtilities.CreateRandom());
            }

            fingerprinter.Add(nameof(Process.RewritePolicy), (byte)process.RewritePolicy);

            if (process.RequiresAdmin)
            {
                fingerprinter.Add(nameof(Process.RequiresAdmin), 1);
            }

            AddPipData(fingerprinter, nameof(Process.Arguments), process.Arguments);
            if (process.ResponseFileData.IsValid)
            {
                AddPipData(fingerprinter, nameof(Process.ResponseFileData), process.ResponseFileData);
            }

            fingerprinter.AddOrderIndependentCollection<EnvironmentVariable, ReadOnlyArray<EnvironmentVariable>>(
                nameof(Process.EnvironmentVariables),
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
                },
                m_environmentVariableComparer
                );

            fingerprinter.Add(nameof(Process.WarningTimeout), process.WarningTimeout.HasValue ? process.WarningTimeout.Value.Ticks : -1);
            fingerprinter.Add(nameof(Process.Timeout), process.Timeout.HasValue ? process.Timeout.Value.Ticks : -1);

            if (process.WarningRegex.IsValid)
            {
                fingerprinter.Add(nameof(Process.WarningRegexPattern), process.WarningRegex.Pattern);
                fingerprinter.Add(nameof(Process.WarningRegexOptions), (int)process.WarningRegex.Options);
            }

            if (process.ErrorRegex.IsValid)
            {
                fingerprinter.Add(nameof(Process.ErrorRegexPattern), process.ErrorRegex.Pattern);
                fingerprinter.Add(nameof(Process.ErrorRegexOptions), (int)process.ErrorRegex.Options);
            }

            fingerprinter.AddOrderIndependentCollection<int, ReadOnlyArray<int>>(nameof(Process.SuccessExitCodes), process.SuccessExitCodes, (h, i) => h.Add(i), Comparer<int>.Default);
            fingerprinter.AddOrderIndependentCollection<int, ReadOnlyArray<int>>(nameof(Process.SucceedFastExitCodes), process.SucceedFastExitCodes, (h, i) => h.Add(i), Comparer<int>.Default);

            if (process.UncacheableExitCodes.Length > 0)
            {
                fingerprinter.AddOrderIndependentCollection<int, ReadOnlyArray<int>>(nameof(Process.UncacheableExitCodes), process.UncacheableExitCodes, (h, i) => h.Add(i), Comparer<int>.Default);
            }

            if (process.ChangeAffectedInputListWrittenFile.IsValid)
            {
                fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>(
                    PipFingerprintField.Process.SourceChangeAffectedInputList,
                    m_sourceChangeAffectedInputsLookup(process).ToReadOnlyArray(),
                    (h, p) => h.Add(p),
                    m_pathTable.ExpandedPathComparer);
                fingerprinter.Add(nameof(Process.ChangeAffectedInputListWrittenFile), process.ChangeAffectedInputListWrittenFile);
            }

            fingerprinter.AddOrderIndependentCollection<IBreakawayChildProcess, ReadOnlyArray<IBreakawayChildProcess>>(
                nameof(Process.ChildProcessesToBreakawayFromSandbox),
                process.ChildProcessesToBreakawayFromSandbox,
                (h, b) => { 
                    h.Add(b.ProcessName.StringId);
                    // Only add the optional arguments if present, so we avoid a general fingerprint bump
                    if (!string.IsNullOrEmpty(b.RequiredArguments))
                    {
                        h.Add(b.RequiredArguments);
                        h.Add(b.RequiredArgumentsIgnoreCase ? 1 : 0);
                    }
                },
                m_breakawayChildProcessComparer);

            fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>(
                nameof(Process.OutputDirectoryExclusions),
                process.OutputDirectoryExclusions, (h, p) => h.Add(p), m_pathTable.ExpandedPathComparer);

            fingerprinter.Add(nameof(Process.PreserveOutputsTrustLevel), process.PreserveOutputsTrustLevel);

            // By default RequireGlobalDependencies is on, and we historically don't track what global passthrough env vars/untracked directories
            // get included as part of it. But whenever this flag is explicitly turned off, make that part of the weak fingerprint.
            if (!process.RequireGlobalDependencies)
            {
                fingerprinter.Add(nameof(Process.RequireGlobalDependencies), 0);
            }
       
            if (process.AllowedUndeclaredSourceReadScopes.Length > 0)
            {
                fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>(
                    nameof(Process.AllowedUndeclaredSourceReadScopes), 
                    process.AllowedUndeclaredSourceReadScopes, 
                    (h, p) => h.Add(p), 
                    m_pathTable.ExpandedPathComparer);
            }

            if (process.AllowedUndeclaredSourceReadPaths.Length > 0)
            {
                fingerprinter.AddOrderIndependentCollection<AbsolutePath, ReadOnlyArray<AbsolutePath>>(
                    nameof(Process.AllowedUndeclaredSourceReadPaths), 
                    process.AllowedUndeclaredSourceReadPaths, 
                    (h, p) => h.Add(p), 
                    m_pathTable.ExpandedPathComparer);
            }

            if (process.AllowedUndeclaredSourceReadRegexes.Length > 0)
            {
                fingerprinter.AddOrderIndependentCollection<RegexDescriptor, IEnumerable<RegexDescriptor>>(
                    nameof(Process.AllowedUndeclaredSourceReadRegexes), 
                    process.AllowedUndeclaredSourceReadRegexes, (h, r) => 
                        { 
                            h.Add(r.Pattern); 
                            h.Add((int)r.Options); 
                        }, 
                    m_regexDescriptorComparer);
            }

            AddProcessSpecificFingerprintSalt(fingerprinter, process);
        }

        /// <summary>
        /// Adds process-specific fingerprint salt to the fingerprint stream.
        /// </summary>
        protected void AddProcessSpecificFingerprintSalt(IFingerprinter fingerprinter, Process process)
        {
            // If the pip has been passed with a specific fingerprinting salt value then we use it in the computation of weak fingerprint.
            var pipFingerprintSaltValue = m_pipFingerprintSaltLookup(process);
            if (!string.IsNullOrEmpty(pipFingerprintSaltValue))
            {
                fingerprinter.Add(
                    PipFingerprintField.Process.ProcessSpecificFingerprintSalt,
                    pipFingerprintSaltValue == "*" ? Guid.NewGuid().ToString() : pipFingerprintSaltValue);
            }

            // If the pip has reclassification rules, we consider them as part of the fingerprint salt,
            // so we mark the pip as dirty both with and without incremental scheduling enabled if these rules change
            if (process.ReclassificationRules.Length > 0)
            {
                fingerprinter.AddCollection<IReclassificationRule, ReadOnlyArray<IReclassificationRule>>(nameof(Process.ReclassificationRules), process.ReclassificationRules, (fp, v) => fp.Add(v.Descriptor()));
            }
        }

        /// <summary>
        /// Adds pip data (such as a command line or the contents of a response file) to a fingerprint stream.
        /// </summary>
        private void AddPipData(IFingerprinter fingerprinter, string name, PipData data)
        {
            Contract.Requires(data.IsValid);
            Contract.Requires(name != null);
            Contract.Requires(fingerprinter != null);

            fingerprinter.Add(name, data.ToString(m_pipFragmentRenderer));
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
                        fp => AddPipData(fp, PipFingerprintField.FileDependency.PathNormalizedWriteFileContent, filePipData));
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
            fingerprinter.AddNested(fileArtifact.Path, fp => fp.Add(PipFingerprintField.FileOutput.Attributes, (int)fileArtifact.FileExistence));
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

        /// <summary>
        /// Creates a hashing helper.
        /// </summary>
        protected HashingHelper CreateHashingHelper(bool useSemanticPaths, HashAlgorithmType hashAlgorithmType = HashAlgorithmType.SHA1Managed)
        {
            return new HashingHelper(
                m_pathTable,
                recordFingerprintString: FingerprintTextEnabled,
                pathExpander: useSemanticPaths ? PathExpander : PathExpander.Default,
                hashAlgorithmType: hashAlgorithmType);
        }

        private IEnumerable<FileArtifact> GetRelevantProcessDependencies(Process process)
        {
            if (!process.UntrackedPaths.IsValid || process.UntrackedPaths.Length == 0)
            {
                return process.Dependencies;
            }

            var untrackedPaths = new HashSet<AbsolutePath>(process.UntrackedPaths);
            
            // Every input files specified as well as untracked paths need to be removed from the dependencies.
            // This ensures that the content of the input file is not part of weak fingerprint. Also, this is aligned with
            // file access manifest interpretation, i.e., all operations on untracked paths are allowed and unreported.
            //
            // The input files are not filtered out against the set of untracked scopes. Even if the input file is within a scope,
            // all accesses to the input file will still be reported because the search for access policy in the file access manifest
            // works bottom up. This setting allows us to untrack some scope but track a few files within that scope.
            return process.Dependencies.Where(f => !untrackedPaths.Contains(f.Path));
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
