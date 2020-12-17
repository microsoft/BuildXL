// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Processes
{
    /// <summary>
    /// Allowlist containing file operations that have been vetted to be safe(ish) but cannot be easily predicted.
    /// </summary>
    public sealed class FileAccessAllowlist
    {
        /// <summary>
        /// Strength of allowlist match.
        /// </summary>
        public enum MatchType
        {
            /// <summary>
            /// File access not matched by any allowlist entry.
            /// </summary>
            NoMatch,

            /// <summary>
            /// File access matched by a rule that allows the access but prevents caching.
            /// </summary>
            MatchesButNotCacheable,

            /// <summary>
            /// File access matched by a rule that allows caching.
            /// </summary>
            MatchesAndCacheable,
        }

        private readonly MultiValueDictionary<FullSymbol, ValuePathFileAccessAllowlistEntry> m_valuePathEntries;
        private readonly MultiValueDictionary<AbsolutePath, ExecutablePathAllowlistEntry> m_executablePathEntries;
        private readonly MultiValueDictionary<StringId, ExecutablePathAllowlistEntry> m_executableToolExeEntries;

        /// <summary>
        /// The parent allowlist
        /// If this is a module specific allowlist, parent is the root allowlist
        /// Otherwise, if this is the root allowlist, this is null
        /// This field is mutually exclusive with the <see cref="m_moduleAllowlists"/> field.
        /// </summary>
        private readonly FileAccessAllowlist m_parent;

        /// <summary>
        /// The module specific allowlists (null if this is a module allowlist)
        /// This field is mutually exclusive with the <see cref="m_parent"/> field.
        /// </summary>
        private readonly Dictionary<ModuleId, FileAccessAllowlist> m_moduleAllowlists;

        private readonly PipExecutionContext m_context;
        private readonly ConcurrentDictionary<string, int> m_counts;

        /// <summary>
        /// Construct an empty allowlist.
        /// </summary>
        public FileAccessAllowlist(PipExecutionContext context)
        {
            Contract.Requires(context != null);

            m_context = context;    

            m_valuePathEntries = new MultiValueDictionary<FullSymbol, ValuePathFileAccessAllowlistEntry>();
            m_executablePathEntries = new MultiValueDictionary<AbsolutePath, ExecutablePathAllowlistEntry>();
            m_executableToolExeEntries = InitializeExecutableToolEntries();
            m_counts = new ConcurrentDictionary<string, int>();
            m_moduleAllowlists = new Dictionary<ModuleId, FileAccessAllowlist>();
        }

        /// <summary>
        /// Construct a nested allowlist.
        /// </summary>
        private FileAccessAllowlist(FileAccessAllowlist parent)
        {
            Contract.Requires(parent != null);

            m_context = parent.m_context;

            m_valuePathEntries = new MultiValueDictionary<FullSymbol, ValuePathFileAccessAllowlistEntry>();
            m_executablePathEntries = new MultiValueDictionary<AbsolutePath, ExecutablePathAllowlistEntry>();
            m_executableToolExeEntries = InitializeExecutableToolEntries();
            m_counts = new ConcurrentDictionary<string, int>();
            m_moduleAllowlists = null;
            m_parent = parent;
        }

        /// <summary>
        /// Determines whether to use a case sensitive comparer for a StringId multivalue dictionary based on whether
        /// the OS uses case sensitive paths
        /// </summary>
        private MultiValueDictionary<StringId, ExecutablePathAllowlistEntry> InitializeExecutableToolEntries()
        {
            MultiValueDictionary<StringId, ExecutablePathAllowlistEntry> entries;

            if (!OperatingSystemHelper.IsPathComparisonCaseSensitive)
            {
                entries = new MultiValueDictionary<StringId, ExecutablePathAllowlistEntry>(m_context.StringTable.CaseInsensitiveEqualityComparer);
            }
            else
            {
                entries = new MultiValueDictionary<StringId, ExecutablePathAllowlistEntry>();
            }

            return entries;
        }

        /// <summary>
        /// Indicates whether the allowlist has any entries.
        /// </summary>
        public bool HasEntries { get; private set; }

        /// <summary>
        /// Constructs a new FileAccessallowlist from the root configuration.
        /// </summary>
        /// <remarks>Throws a BuildXLException on error.</remarks>
        public void Initialize(IRootModuleConfiguration rootConfiguration)
        {
            Contract.Assert(m_parent == null, "Only root allowlist can be initialized");

            Initialize((IModuleConfiguration)rootConfiguration);

            foreach (var module in rootConfiguration.ModulePolicies.Values)
            {
                if ((module.FileAccessAllowList.Count == 0) &&
                    (module.CacheableFileAccessAllowList.Count == 0))
                {
                    continue;
                }

                var moduleAllowlist = new FileAccessAllowlist(this);
                moduleAllowlist.Initialize(module);
                m_moduleAllowlists.Add(module.ModuleId, moduleAllowlist);
            }
        }

        /// <summary>
        /// Gets the allowlist for the given module
        /// </summary>
        public FileAccessAllowlist GetModuleAllowlist(ModuleId moduleId)
        {
            Contract.Assert(m_parent == null, "Only root allowlist can be queried for module allowlists");

            FileAccessAllowlist moduleAllowlist;
            if (m_moduleAllowlists.TryGetValue(moduleId, out moduleAllowlist))
            {
                return moduleAllowlist;
            }

            return this;
        }

        private void Initialize(IModuleConfiguration configuration)
        {
            if (configuration.FileAccessAllowList != null)
            {
                foreach (var allowlistEntry in configuration.FileAccessAllowList)
                {
                    AddAllowListEntry(allowlistEntry, false);
                }
            }

            if (configuration.CacheableFileAccessAllowList != null)
            {
                foreach (var allowlistEntry in configuration.CacheableFileAccessAllowList)
                {
                    AddAllowListEntry(allowlistEntry, true);
                }
            }
        }

        private void AddAllowListEntry(IFileAccessAllowlistEntry allowlistEntry, bool allowsCaching)
        {
            SerializableRegex pathRegex;
            string regexError;
            if (string.IsNullOrEmpty(allowlistEntry.PathRegex))
            {
                if (!TryCreateAllowlistRegex(Regex.Escape(allowlistEntry.PathFragment), out pathRegex, out regexError))
                {
                    throw new BuildXLException("An allowlist regex should never fail to construct from an escaped pattern: " + regexError);
                }
            }
            else
            {
                if (!TryCreateAllowlistRegex(allowlistEntry.PathRegex, out pathRegex, out regexError))
                {
                    throw new BuildXLException("A regex should have already been validated when parsed: " + regexError);
                }
            }

            if (!string.IsNullOrEmpty(allowlistEntry.Value))
            {
                Add(
                    new ValuePathFileAccessAllowlistEntry(
                        outputValue: FullSymbol.Create(m_context.SymbolTable, allowlistEntry.Value),
                        pathRegex: pathRegex,
                        allowsCaching: allowsCaching,
                        name: allowlistEntry.Name));
            }
            else
            {
                object toolPath = allowlistEntry.ToolPath.GetValue();
                var executablePath =
                    toolPath switch 
                    {
                        FileArtifact file => new DiscriminatingUnion<AbsolutePath, PathAtom>(file.Path),
                        PathAtom toolName => new DiscriminatingUnion<AbsolutePath, PathAtom>(toolName),
                        _ => null,
                    };

                Contract.RequiresNotNull(executablePath);

                Add(
                    new ExecutablePathAllowlistEntry(
                        executable: executablePath,
                        pathRegex: pathRegex,
                        allowsCaching: allowsCaching,
                        name: allowlistEntry.Name));
            }
        }

        /// <summary>
        /// Add a single allowlist entry to the list.
        /// </summary>
        public void Add(ValuePathFileAccessAllowlistEntry entry)
        {
            Contract.Requires(entry != null);

            m_valuePathEntries.Add(entry.OutputValue, entry);
            m_counts.AddOrUpdate(entry.Name, 0, (k, v) => v);
            HasEntries = true;
        }

        /// <summary>
        /// Add a single allowlist entry to the list.
        /// </summary>
        public void Add(ExecutablePathAllowlistEntry entry)
        {
            Contract.Requires(entry != null);

            var toolPath = entry.Executable.GetValue();

            if (toolPath is AbsolutePath absolutePath)
            {
                m_executablePathEntries.Add(absolutePath, entry);
            }
            else if (toolPath is PathAtom pathAtom)
            {
                m_executableToolExeEntries.Add(pathAtom.StringId, entry);
            }

            m_counts.AddOrUpdate(entry.Name, 0, (k, v) => v);
            HasEntries = true;
        }

        /// <summary>
        /// Returns the strength of the strongest match in the allowlist for a given file access.
        /// </summary>
        /// <remarks>
        /// A allowlist entry must match in value name, allowing this path to return false quickly after a failed Dictionary lookup
        /// in most cases.
        /// </remarks>
        public MatchType Matches(LoggingContext loggingContext, ReportedFileAccess reportedFileAccess, Process pip)
        {
            Contract.Requires(pip != null);

            IEnumerable<FileAccessAllowlistEntry> possibleEntries = new List<FileAccessAllowlistEntry>();

            ConcatPossibleEntries(loggingContext, reportedFileAccess, pip, ref possibleEntries);

            if (m_parent != null)
            {
                m_parent.ConcatPossibleEntries(loggingContext, reportedFileAccess, pip, ref possibleEntries);
            }

            var strongestMatch = MatchType.NoMatch;

            foreach (FileAccessAllowlistEntry allowlistEntry in possibleEntries)
            {
                MatchType entryMatchType = allowlistEntry.Matches(reportedFileAccess, pip, m_context.PathTable);
                switch (entryMatchType)
                {
                    case MatchType.MatchesAndCacheable:
                        m_counts.AddOrUpdate(allowlistEntry.Name, 1, (k, v) => v + 1);
                        strongestMatch = MatchType.MatchesAndCacheable;
                        break;
                    case MatchType.MatchesButNotCacheable:
                        m_counts.AddOrUpdate(allowlistEntry.Name, 1, (k, v) => v + 1);
                        strongestMatch = strongestMatch == MatchType.MatchesAndCacheable ? strongestMatch : MatchType.MatchesButNotCacheable;
                        break;
                    default:
                        Contract.Assert(entryMatchType == MatchType.NoMatch);

                        // No-op; either we've already previously found a match or leave it as NoMatch.
                        break;
                }
            }

            return strongestMatch;
        }

        private void ConcatPossibleEntries(LoggingContext loggingContext, ReportedFileAccess reportedFileAccess, Process pip, ref IEnumerable<FileAccessAllowlistEntry> possibleEntries)
        {
            IReadOnlyList<ValuePathFileAccessAllowlistEntry> valuePathAllowlistList;
            if (m_valuePathEntries.TryGetValue(pip.Provenance.OutputValueSymbol, out valuePathAllowlistList))
            {
                possibleEntries = possibleEntries.Concat(valuePathAllowlistList);
            }

            AbsolutePath toolPath;
            int characterWithError;
            if (AbsolutePath.TryCreate(m_context.PathTable, reportedFileAccess.Process.Path, out toolPath, out characterWithError) != AbsolutePath.ParseResult.Success)
            {
                BuildXL.Processes.Tracing.Logger.Log.FileAccessAllowlistFailedToParsePath(
                    loggingContext,
                    pip.SemiStableHash,
                    pip.GetDescription(m_context),
                    reportedFileAccess.Describe(),
                    reportedFileAccess.Process.Path,
                    characterWithError);
            }
            else
            {
                PathAtom toolExecutableName = toolPath.GetName(m_context.PathTable);
                IReadOnlyList<ExecutablePathAllowlistEntry> executablePathAllowlistList;

                if (m_executablePathEntries.TryGetValue(toolPath, out executablePathAllowlistList) || 
                    m_executableToolExeEntries.TryGetValue(toolExecutableName.StringId, out executablePathAllowlistList))
                {
                    possibleEntries = possibleEntries.Concat(executablePathAllowlistList);
                }
            }
        }

        /// <summary>
        /// Serializes
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            SerializeCore(writer);

            writer.WriteCompact(m_moduleAllowlists.Count);
            foreach (var moduleAllowistEntry in m_moduleAllowlists)
            {
                writer.Write(moduleAllowistEntry.Key);
                moduleAllowistEntry.Value.SerializeCore(writer);
            }
        }

        private void SerializeCore(BuildXLWriter writer)
        {
            ValuePathFileAccessAllowlistEntry[] valuePathEntries = m_valuePathEntries.Values.SelectMany((e) => { return e; }).ToArray();
            writer.WriteCompact(valuePathEntries.Length);
            foreach (ValuePathFileAccessAllowlistEntry entry in valuePathEntries)
            {
                entry.Serialize(writer);
            }

            var pathEntries = m_executablePathEntries.Values.SelectMany((e) => { return e; });
            var toolEntries = m_executableToolExeEntries.Values.SelectMany((e) => { return e; });
            ExecutablePathAllowlistEntry[] entries = pathEntries.Concat(toolEntries).ToArray();
            writer.WriteCompact(entries.Length);
            foreach (ExecutablePathAllowlistEntry entry in entries)
            {
                entry.Serialize(writer);
            }
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static async Task<FileAccessAllowlist> DeserializeAsync(
            BuildXLReader reader,
            Task<PipExecutionContext> contextTask)
        {
            Contract.Requires(reader != null);
            Contract.Requires(contextTask != null);

            var context = await contextTask;
            if (context == null)
            {
                return null;
            }

            var result = new FileAccessAllowlist(context);
            DeserializeCore(reader, result);

            var moduleAllowlistCount = reader.ReadInt32Compact();
            for (int j = 0; j < moduleAllowlistCount; j++)
            {
                var moduleId = reader.ReadModuleId();
                FileAccessAllowlist moduleAllowlist = new FileAccessAllowlist(result);
                DeserializeCore(reader, moduleAllowlist);

                result.m_moduleAllowlists.Add(moduleId, moduleAllowlist);
            }

            return result;
        }

        private static void DeserializeCore(BuildXLReader reader, FileAccessAllowlist allowlist)
        {
            var valuePathEntryCount = reader.ReadInt32Compact();
            for (int i = 0; i < valuePathEntryCount; i++)
            {
                allowlist.Add(ValuePathFileAccessAllowlistEntry.Deserialize(reader));
            }

            // Execute this part twice, first time for m_executablePathEntries (Absolute Path) and a second time for m_executablePathAtomEntries (Path Atom)
            var executablePathEntryCount = reader.ReadInt32Compact();
            for (int j = 0; j < executablePathEntryCount; j++)
            {
                allowlist.Add(ExecutablePathAllowlistEntry.Deserialize(reader));
            }
        }

        /// <summary>
        /// Match a given access-rule fragment against a ReportedFileAccess.
        /// </summary>
        /// <remarks>
        /// Case insensitive since our first target is Windows, which has case-insensitive filesystem semantics.  That decision
        /// might need to be revisited in the future.
        /// </remarks>
        internal static bool PathFilterMatches(
            Regex pathRegex,
            ReportedFileAccess reportedFileAccess,
            PathTable pathTable)
        {
            Contract.Requires((pathRegex.Options & RegexOptions.Compiled) != 0);
            Contract.Requires((pathRegex.Options & RegexOptions.IgnoreCase) != 0);
            Contract.Requires((pathRegex.Options & RegexOptions.CultureInvariant) != 0);

            return pathRegex.IsMatch(reportedFileAccess.GetPath(pathTable));
        }

        internal static MatchType Match(bool matches, bool entryAllowsCaching)
        {
            if (matches)
            {
                if (entryAllowsCaching)
                {
                    return MatchType.MatchesAndCacheable;
                }

                return MatchType.MatchesButNotCacheable;
            }

            return MatchType.NoMatch;
        }

        /// <summary>
        /// Exception that encapsulates the Regex constructor's ArgumentException.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2237:MarkISerializableTypesWithSerializable",
            Justification = "We don't need exceptions to cross AppDomain boundaries.")]
        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors",
            Justification = "This exception is constructed in exactly one place, below.")]
        public sealed class BadRegexException : Exception
        {
            /// <summary>
            /// Construct a new BadRegexException from an ArgumentException thrown by <code>new Regex</code>.
            /// </summary>
            public BadRegexException(string message, ArgumentException innerException)
                : base(message, innerException)
            {
            }
        }

        /// <summary>
        /// Make a regex to match the given string path fragment.
        /// </summary>
        internal static SerializableRegex RegexWithProperties(string pattern)
        {
            Contract.Requires(!string.IsNullOrEmpty(pattern));

            Contract.Ensures((Contract.Result<SerializableRegex>().Regex.Options & RegexOptions.Compiled) != 0);
            Contract.Ensures((Contract.Result<SerializableRegex>().Regex.Options & RegexOptions.IgnoreCase) != 0);
            Contract.Ensures((Contract.Result<SerializableRegex>().Regex.Options & RegexOptions.CultureInvariant) != 0);

            return new SerializableRegex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Make a regex to match the given string path fragment.
        /// </summary>
        public static bool TryCreateAllowlistRegex(string pattern, out SerializableRegex allowlistRegex, out string error)
        {
            Contract.Requires(!string.IsNullOrEmpty(pattern), "Regex pattern must not be null or empty.");

            allowlistRegex = null;
            error = null;

            try
            {
                allowlistRegex = RegexWithProperties(pattern);
            }
            catch (ArgumentException e)
            {
                error = e.Message;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Count of entries that allow caching
        /// </summary>
        public int CacheableEntryCount
        {
            get
            {
                return m_valuePathEntries.SelectMany(e => e.Value.Where(e2 => e2.AllowsCaching)).Count() +
                    m_executablePathEntries.SelectMany(e => e.Value.Where(e2 => e2.AllowsCaching)).Count() +
                    m_executableToolExeEntries.SelectMany(e => e.Value.Where(e2 => e2.AllowsCaching)).Count();
            }
        }

        /// <summary>
        /// Count of entries that do not allow caching
        /// </summary>
        public int UncacheableEntryCount
        {
            get
            {
                return m_valuePathEntries.SelectMany(e => e.Value.Where(e2 => !e2.AllowsCaching)).Count() +
                    m_executablePathEntries.SelectMany(e => e.Value.Where(e2 => !e2.AllowsCaching)).Count() +
                    m_executableToolExeEntries.SelectMany(e => e.Value.Where(e2 => !e2.AllowsCaching)).Count();
            }
        }

        /// <summary>
        /// Dictionary of allowlist entries that matched to file accesses and their counts.
        /// </summary>
        public IDictionary<string, int> MatchedEntryCounts => m_counts;

        /// <summary>
        /// ValuePathEntries collection. For testing only
        /// </summary>
        internal IReadOnlyDictionary<FullSymbol, IReadOnlyList<ValuePathFileAccessAllowlistEntry>> ValuePathEntries => m_valuePathEntries;

        /// <summary>
        /// ExecutablePathEntries collection. For testing only
        /// </summary>
        internal IReadOnlyDictionary<AbsolutePath, IReadOnlyList<ExecutablePathAllowlistEntry>> ExecutablePathEntries => m_executablePathEntries;

        /// <summary>
        /// ExecutablePathAtomEntries collection. For testing only
        /// </summary>
        internal IReadOnlyDictionary<StringId, IReadOnlyList<ExecutablePathAllowlistEntry>> ToolExecutableNameEntries => m_executableToolExeEntries;

        /// <summary>
        /// The per-module allowlists (may be null)
        /// </summary>
        public IReadOnlyDictionary<ModuleId, FileAccessAllowlist> ModuleAllowlists => m_moduleAllowlists;
    }
}
