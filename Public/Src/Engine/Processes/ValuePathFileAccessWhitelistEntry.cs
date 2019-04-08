// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// A specific whitelist entry, consisting of a set of filters to test reported file operations against.
    /// </summary>
    public sealed class ValuePathFileAccessWhitelistEntry : FileAccessWhitelistEntry
    {
        private readonly FullSymbol m_outputValue;

        /// <summary>
        /// The value associated with the whitelist entry.
        /// </summary>
        public FullSymbol OutputValue => m_outputValue;

        /// <summary>
        /// Construct a new whitelist entry.
        /// </summary>
        /// <param name="outputValue">BuildXL value name associated with this entry.  This will be *exactly* matched.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this whitelist rule should be interpreted to allow caching of a pip that matches
        /// it.
        /// </param>
        /// <param name="name">Name of the whitelist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ValuePathFileAccessWhitelistEntry(FullSymbol outputValue, SerializableRegex pathRegex, bool allowsCaching, string name)
            : base(pathRegex, allowsCaching, name)
        {
            Contract.Requires(outputValue.IsValid);
            Contract.Requires(pathRegex != null);

            m_outputValue = outputValue;
        }

        /// <summary>
        /// Determine whether a ReportedFileAccess matches the whitelist rules.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011", Justification = "Only a Process can have unknown file accesses.")]
        public override FileAccessWhitelist.MatchType Matches(ReportedFileAccess reportedFileAccess, Process pip, PathTable pathTable)
        {
            Contract.Requires(pip != null);
            Contract.Requires(pathTable != null);

            // An access is whitelisted if it:
            // * Is an exact value-symbol match (implicit here by lookup from FileAccessWhitelist.Matches), AND
            // * the path filter matches (or is empty).
            return FileAccessWhitelist.Match(FileAccessWhitelist.PathFilterMatches(PathRegex.Regex, reportedFileAccess, pathTable), AllowsCaching);
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            WriteState(writer);
            writer.Write(m_outputValue);
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static ValuePathFileAccessWhitelistEntry Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var state = ReadState(reader);
            FullSymbol symbol = reader.ReadFullSymbol();

            return new ValuePathFileAccessWhitelistEntry(
                symbol,
                state.PathRegex,
                state.AllowsCaching,
                state.Name);
        }
    }
}
