// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace BuildXL.Utilities.PackedTable
{
    /// <summary>
    /// Create a filter that searches for entries by (potentially multi-part) name.
    /// </summary>
    /// <remarks>
    /// Specifically the filter supports filtering entries by either:
    /// - one substring of one atom, or
    /// - multiple atoms, matched by suffix on the first atom, by prefix on the last, and
    ///   exactly for all in between.
    ///   
    /// Effectively, the filter string is matched as if it were a substring of the entire
    /// expanded name.
    /// 
    /// No wildcards are supported yet.
    /// 
    /// So for example, in the pip name scenario where the delimiter is '.':
    /// - Searching for "a" will find all pips that have any atoms with "a" or "A"
    ///   in their names.
    /// - Searching for "3.x" will find all pips that have an atom ending with "3",
    ///   followed by an atom starting with "x" or "X".
    /// - Searching for "foo.bar.pass3" will find all pips that have an atom ending
    ///   with "foo", followed by the atom "bar", followed by an atom starting with "pass3"
    ///   (all case-insensitively).
    /// </remarks>
    public class NameFilter<TId>
        where TId : unmanaged, Id<TId>
    {
        /// <summary>Type of substring match, based on location of the substring in the overall match string.</summary>
        private enum MatchType
        {
            /// <summary>Match the atom exactly.</summary>
            Equals,

            /// <summary>Match the end of the atom.</summary>
            EndsWith,

            /// <summary>Match the start of the atom.</summary>
            StartsWith,

            /// <summary>Match anywhere in the atom.</summary>
            Contains,
        }

        /// <summary>The base table we're filtering.</summary>
        private readonly ITable<TId> m_table;

        /// <summary>The name index we're filtering.</summary>
        private readonly NameIndex m_nameIndex;

        /// <summary>Mapping from a table ID to the name of that table entry.</summary>
        private readonly Func<TId, NameId> m_namerFunc;

        /// <summary>The delimiter between parts of a name.</summary>
        /// <remarks>Typically either '.' (for pip names) or '\' (for paths)</remarks>
        private readonly char m_delimiter;

        /// <summary>The string being matched.</summary>
        private readonly string m_matchString;

        /// <summary>Construct a NameFilter.</summary>
        /// <param name="table">The table with the entries being filtered.</param>
        /// <param name="nameIndex">The name index containing the names we will filter.</param>
        /// <param name="namerFunc">Function to obtain a name ID from a table ID.</param>
        /// <param name="delimiter">The character delimiter applicable to this filter.</param>
        /// <param name="matchString">The (possibly delimited) match string.</param>
        public NameFilter(ITable<TId> table, NameIndex nameIndex, Func<TId, NameId> namerFunc, char delimiter, string matchString)
        {
            m_table = table;
            m_nameIndex = nameIndex;
            m_namerFunc = namerFunc;
            m_delimiter = delimiter;
            m_matchString = matchString;
        }

        /// <summary>Actually perform the filtering and return the result.</summary>
        public IEnumerable<TId> Filter()
        {
            // Break up the match string into delimited pieces, and get all string atoms matching each piece.
            IEnumerable<IEnumerable<StringId>> matches = GetMatchingAtoms();

            // Now we have a LIST of bags of StringIds. We now want to filter all names in the index for names
            // which have a sequence of atoms that are contained in each respective bag in the sequence.
            // We therefore really want HashSets here rather than ConcurrentBags.
            List<HashSet<StringId>> matchSets = new List<HashSet<StringId>>();
            foreach (IEnumerable<StringId> bag in matches)
            {
                matchSets.Add(new HashSet<StringId>(bag));
            }

            // Now, in parallel (and unordered), traverse all names in the index to find ones which match.
            HashSet<NameId> matchingNames = m_nameIndex.Ids
                .AsParallel()
                .Where(nid =>
                {
                    ReadOnlySpan<NameEntry> atoms = m_nameIndex[nid];
                    if (atoms.Length < matchSets.Count)
                    {
                        // not enough atoms to be a match; continue
                        return false;
                    }

                    // we match on the matchSets starting at the end of the name. This is because we expect names to
                    // be more similar towards their beginnings (e.g. names share prefixes more than suffixes), so
                    // matching from the end should reject more names more quickly.
                    // i is the index into the start of the atoms subsequence being matched; j is the index into matchSets.
                    for (int i = atoms.Length - matchSets.Count; i >= 0; i--)
                    {
                        bool isMatch = true;
                        for (int j = matchSets.Count - 1; j >= 0; j--)
                        {
                            HashSet<StringId> matchSet = matchSets[j];
                            isMatch = matchSet.Contains(atoms[i + j].Atom);

                            if (!isMatch)
                            {
                                break;
                            }
                        }

                        if (isMatch)
                        {
                            return true;
                        }
                    }

                    // no match found
                    return false;
                })
                .ToHashSet();

            // Now we need to traverse the whole original table, finding the IDs for the names we matched.
            IEnumerable<TId> result = m_table.Ids
                .AsParallel()
                .Where(id => matchingNames.Contains(m_namerFunc(id)));

            return result;
        }

        /// <summary>Split the delimited match string, and find all the atoms that match each part of the split.</summary>
        private IEnumerable<IEnumerable<StringId>> GetMatchingAtoms()
        {
            // First decompose the match string by delimiter.
            string[] matchPieces = m_matchString.Trim().Split(m_delimiter);
            // The sub-pieces of each match: the kind of match, the string to match, and the bag to store the matching StringIds.
            // Note that since we will filter each string only once, we will wind up with no duplicates in any bags.
            List<(MatchType matchType, string toMatch, ConcurrentBag<StringId> bag)> matches 
                = new List<(MatchType, string, ConcurrentBag<StringId>)>();

            if (matchPieces.Length == 0)
            {
                // we treat this as "no matches" (more useful than "match everything")
                // but really this should be caught in the UI
                return new List<ConcurrentBag<StringId>>();
            }

            if (matchPieces.Length == 1)
            {
                // match a substring of any atom
                matches.Add((MatchType.Contains, matchPieces[0], new ConcurrentBag<StringId>()));
            }
            else
            {
                for (int i = 0; i < matchPieces.Length; i++)
                {
                    MatchType matchType = i == 0 
                        ? MatchType.EndsWith 
                        : i == matchPieces.Length - 1 
                            ? MatchType.StartsWith
                            : MatchType.Equals;

                    matches.Add((matchType, matchPieces[i], new ConcurrentBag<StringId>()));
                }
            }

            // Now scan the whole string table in parallel.
            StringTable stringTable = m_nameIndex.NameTable.StringTable;
            stringTable.Ids.AsParallel().ForAll(sid =>
            {
                ReadOnlySpan<char> atom = stringTable[sid];
                for (int i = 0; i < matches.Count; i++)
                {
                    bool isMatch = matches[i].matchType switch
                    {
                        MatchType.Contains => MemoryExtensions.Contains(atom, matches[i].toMatch.AsSpan(), StringComparison.InvariantCultureIgnoreCase),
                        MatchType.StartsWith => MemoryExtensions.StartsWith(atom, matches[i].toMatch.AsSpan(), StringComparison.InvariantCultureIgnoreCase),
                        MatchType.EndsWith => MemoryExtensions.EndsWith(atom, matches[i].toMatch.AsSpan(), StringComparison.InvariantCultureIgnoreCase),
                        MatchType.Equals => MemoryExtensions.Equals(atom, matches[i].toMatch.AsSpan(), StringComparison.InvariantCultureIgnoreCase),
                        _ => throw new InvalidOperationException($"Unknown MatchType {matches[i].Item1}"),
                    };
                    if (isMatch)
                    {
                        matches[i].bag.Add(sid);
                    }
                }
            });

            // now all the bags have any and all matching string IDs for those atoms in the name.
            // Return only the bags.
            return matches.Select(tuple => tuple.bag);
        }
    }
}
