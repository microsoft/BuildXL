// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Processes
{
    /// <summary>
    /// Whitelist containing file operations that have been vetted to be safe(ish) but cannot be easily predicted.
    /// </summary>
    public sealed class FileAccessWhitelist
    {
        /// <summary>
        /// Strength of whitelist match.
        /// </summary>
        public enum MatchType
        {
            /// <summary>
            /// File access not matched by any whitelist entry.
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

        private readonly MultiValueDictionary<FullSymbol, ValuePathFileAccessWhitelistEntry> m_valuePathEntries;
        private readonly MultiValueDictionary<AbsolutePath, ExecutablePathWhitelistEntry> m_executablePathEntries;

        /// <summary>
        /// The parent whitelist
        /// If this is a module specific whitelist, parent is the root whitelist
        /// Otherwise, if this is the root whitelist, this is null
        /// This field is mutually exclusive with the <see cref="m_moduleWhitelists"/> field.
        /// </summary>
        private readonly FileAccessWhitelist m_parent;

        /// <summary>
        /// The module specific whitelists (null if this is a module whitelist)
        /// This field is mutually exclusive with the <see cref="m_parent"/> field.
        /// </summary>
        private readonly Dictionary<ModuleId, FileAccessWhitelist> m_moduleWhitelists;

        private readonly PipExecutionContext m_context;
        private readonly ConcurrentDictionary<string, int> m_counts;

        /// <summary>
        /// Construct an empty whitelist.
        /// </summary>
        public FileAccessWhitelist(PipExecutionContext context)
        {
            Contract.Requires(context != null);

            m_context = context;

            m_valuePathEntries = new MultiValueDictionary<FullSymbol, ValuePathFileAccessWhitelistEntry>();
            m_executablePathEntries = new MultiValueDictionary<AbsolutePath, ExecutablePathWhitelistEntry>();
            m_counts = new ConcurrentDictionary<string, int>();
            m_moduleWhitelists = new Dictionary<ModuleId, FileAccessWhitelist>();
        }

        /// <summary>
        /// Construct a nested whitelist.
        /// </summary>
        private FileAccessWhitelist(FileAccessWhitelist parent)
        {
            Contract.Requires(parent != null);

            m_context = parent.m_context;

            m_valuePathEntries = new MultiValueDictionary<FullSymbol, ValuePathFileAccessWhitelistEntry>();
            m_executablePathEntries = new MultiValueDictionary<AbsolutePath, ExecutablePathWhitelistEntry>();
            m_counts = new ConcurrentDictionary<string, int>();
            m_moduleWhitelists = null;
            m_parent = parent;
        }

        /// <summary>
        /// Indicates whether the whitelist has any entries.
        /// </summary>
        public bool HasEntries { get; private set; }

        private void AddWhiteListEntry(IFileAccessWhitelistEntry whitelistEntry, bool allowsCaching)
        {
            SerializableRegex pathRegex;
            string regexError;
            if (string.IsNullOrEmpty(whitelistEntry.PathRegex))
            {
                if (!TryCreateWhitelistRegex(Regex.Escape(whitelistEntry.PathFragment), out pathRegex, out regexError))
                {
                    throw new BuildXLException("A whitelist regex should never fail to construct from an escaped pattern: " + regexError);
                }
            }
            else
            {
                if (!TryCreateWhitelistRegex(whitelistEntry.PathRegex, out pathRegex, out regexError))
                {
                    throw new BuildXLException("A regex should have already been validated when parsed: " + regexError);
                }
            }

            if (!string.IsNullOrEmpty(whitelistEntry.Value))
            {
                Add(
                    new ValuePathFileAccessWhitelistEntry(
                        outputValue: FullSymbol.Create(m_context.SymbolTable, whitelistEntry.Value),
                        pathRegex: pathRegex,
                        allowsCaching: allowsCaching,
                        name: whitelistEntry.Name));
            }
            else
            {
                Add(
                    new ExecutablePathWhitelistEntry(
                        executablePath: whitelistEntry.ToolPath,
                        pathRegex: pathRegex,
                        allowsCaching: allowsCaching,
                        name: whitelistEntry.Name));
            }
        }

        /// <summary>
        /// Add a single whitelist entry to the list.
        /// </summary>
        public void Add(ValuePathFileAccessWhitelistEntry entry)
        {
            Contract.Requires(entry != null);

            m_valuePathEntries.Add(entry.OutputValue, entry);
            m_counts.AddOrUpdate(entry.Name, 0, (k, v) => v);
            HasEntries = true;
        }

        /// <summary>
        /// Add a single whitelist entry to the list.
        /// </summary>
        public void Add(ExecutablePathWhitelistEntry entry)
        {
            Contract.Requires(entry != null);

            m_executablePathEntries.Add(entry.ExecutablePath, entry);
            m_counts.AddOrUpdate(entry.Name, 0, (k, v) => v);
            HasEntries = true;
        }

        /// <summary>
        /// Returns the strength of the strongest match in the whitelist for a given file access.
        /// </summary>
        /// <remarks>
        /// A whitelist entry must match in value name, allowing this path to return false quickly after a failed Dictionary lookup
        /// in most cases.
        /// </remarks>
        public MatchType Matches(ReportedFileAccess reportedFileAccess, Process pip)
        {
            Contract.Requires(pip != null);

            IEnumerable<FileAccessWhitelistEntry> possibleEntries = new List<FileAccessWhitelistEntry>();

            ConcatPossibleEntries(reportedFileAccess, pip, ref possibleEntries);

            if (m_parent != null)
            {
                m_parent.ConcatPossibleEntries(reportedFileAccess, pip, ref possibleEntries);
            }

            var strongestMatch = MatchType.NoMatch;

            foreach (FileAccessWhitelistEntry whitelistEntry in possibleEntries)
            {
                MatchType entryMatchType = whitelistEntry.Matches(reportedFileAccess, pip, m_context.PathTable);
                switch (entryMatchType)
                {
                    case MatchType.MatchesAndCacheable:
                        m_counts.AddOrUpdate(whitelistEntry.Name, 1, (k, v) => v + 1);
                        strongestMatch = MatchType.MatchesAndCacheable;
                        break;
                    case MatchType.MatchesButNotCacheable:
                        m_counts.AddOrUpdate(whitelistEntry.Name, 1, (k, v) => v + 1);
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

        private void ConcatPossibleEntries(ReportedFileAccess reportedFileAccess, Process pip, ref IEnumerable<FileAccessWhitelistEntry> possibleEntries)
        {
            IReadOnlyList<ValuePathFileAccessWhitelistEntry> valuePathWhitelistList;
            if (m_valuePathEntries.TryGetValue(pip.Provenance.OutputValueSymbol, out valuePathWhitelistList))
            {
                possibleEntries = possibleEntries.Concat(valuePathWhitelistList);
            }

            AbsolutePath toolPath;
            int characterWithError;
            if (AbsolutePath.TryCreate(m_context.PathTable, reportedFileAccess.Process.Path, out toolPath, out characterWithError) != AbsolutePath.ParseResult.Success)
            {
                BuildXL.Processes.Tracing.Logger.Log.FileAccessWhitelistFailedToParsePath(
                    Events.StaticContext,
                    pip.SemiStableHash,
                    pip.GetDescription(m_context),
                    reportedFileAccess.Describe(),
                    reportedFileAccess.Process.Path,
                    characterWithError);
            }
            else
            {
                IReadOnlyList<ExecutablePathWhitelistEntry> executablePathWhitelistList;
                if (m_executablePathEntries.TryGetValue(toolPath, out executablePathWhitelistList))
                {
                    possibleEntries = possibleEntries.Concat(executablePathWhitelistList);
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

            writer.WriteCompact(m_moduleWhitelists.Count);
            foreach (var moduleWhitelistEntry in m_moduleWhitelists)
            {
                writer.Write(moduleWhitelistEntry.Key);
                moduleWhitelistEntry.Value.SerializeCore(writer);
            }
        }

        private void SerializeCore(BuildXLWriter writer)
        {
            ValuePathFileAccessWhitelistEntry[] valuePathEntries = m_valuePathEntries.Values.SelectMany((e) => { return e; }).ToArray();
            writer.WriteCompact(valuePathEntries.Length);
            foreach (ValuePathFileAccessWhitelistEntry entry in valuePathEntries)
            {
                entry.Serialize(writer);
            }

            ExecutablePathWhitelistEntry[] executablePathEntries = m_executablePathEntries.Values.SelectMany((e) => { return e; }).ToArray();
            writer.WriteCompact(executablePathEntries.Length);
            foreach (ExecutablePathWhitelistEntry entry in executablePathEntries)
            {
                entry.Serialize(writer);
            }
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static async Task<FileAccessWhitelist> DeserializeAsync(
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

            var result = new FileAccessWhitelist(context);
            DeserializeCore(reader, result);

            var moduleWhitelistCount = reader.ReadInt32Compact();
            for (int j = 0; j < moduleWhitelistCount; j++)
            {
                var moduleId = reader.ReadModuleId();
                FileAccessWhitelist moduleWhitelist = new FileAccessWhitelist(result);
                DeserializeCore(reader, moduleWhitelist);

                result.m_moduleWhitelists.Add(moduleId, moduleWhitelist);
            }

            return result;
            }

        private static void DeserializeCore(BuildXLReader reader, FileAccessWhitelist whitelist)
        {
            var valuePathEntryCount = reader.ReadInt32Compact();
            for (int i = 0; i < valuePathEntryCount; i++)
            {
                whitelist.Add(ValuePathFileAccessWhitelistEntry.Deserialize(reader));
            }

            var executablePathEntryCount = reader.ReadInt32Compact();
            for (int i = 0; i < executablePathEntryCount; i++)
            {
                whitelist.Add(ExecutablePathWhitelistEntry.Deserialize(reader));
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
        public static bool TryCreateWhitelistRegex(string pattern, out SerializableRegex whitelistRegex, out string error)
        {
            Contract.Requires(!string.IsNullOrEmpty(pattern), "Regex pattern must not be null or empty.");

            whitelistRegex = null;
            error = null;

            try
            {
                whitelistRegex = RegexWithProperties(pattern);
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
                    m_executablePathEntries.SelectMany(e => e.Value.Where(e2 => e2.AllowsCaching)).Count();
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
                    m_executablePathEntries.SelectMany(e => e.Value.Where(e2 => !e2.AllowsCaching)).Count();
            }
        }

        /// <summary>
        /// Dictionary of whitelist entries that matched to file accesses and their counts.
        /// </summary>
        public IDictionary<string, int> MatchedEntryCounts => m_counts;

        /// <summary>
        /// ValuePathEntries collection. For testing only
        /// </summary>
        internal IReadOnlyDictionary<FullSymbol, IReadOnlyList<ValuePathFileAccessWhitelistEntry>> ValuePathEntries => m_valuePathEntries;

        /// <summary>
        /// ExecutablePathEntries collection. For testing only
        /// </summary>
        internal IReadOnlyDictionary<AbsolutePath, IReadOnlyList<ExecutablePathWhitelistEntry>> ExecutablePathEntries => m_executablePathEntries;

        /// <summary>
        /// The per-module whitelists (may be null)
        /// </summary>
        public IReadOnlyDictionary<ModuleId, FileAccessWhitelist> ModuleWhitelists => m_moduleWhitelists;
    }
}
