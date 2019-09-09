// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Storage;
using BuildXL.Utilities;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// This class is in charge of rendering <see cref="PipData"/> to string.
    /// </summary>
    /// <remarks>
    /// Strongly Immutable.
    /// </remarks>
    public sealed class PipFragmentRenderer
    {
        /// <summary>
        /// Refers to a function which maps file artifacts to (content-hash, file-length) tuples.
        /// This is used to determine the content hashes of pip dependencies.
        /// </summary>
        public delegate FileContentInfo ContentHashLookup(FileArtifact artifact);

        /// <summary>
        /// String representation of a <see cref="FileContentInfo"/> with zero hash and unknonw file length.
        /// </summary>
        /// <remarks>
        /// Used for rendering of <see cref="PipFragmentType.VsoHash"/> fragment only when no <see cref="HashLookup"/> is provided.
        /// </remarks>
        private static readonly string s_unknownLengthFileInfoString = FileContentInfo.CreateWithUnknownLength(ContentHashingUtilities.ZeroHash).Render();

        /// <summary>
        /// Maximum possible length of a rendered <see cref="PipFragmentType.VsoHash"/> fragment.
        /// </summary>
        /// <remarks>
        /// Used only for approximating the max length of the rendered command line string. (<see cref="GetMaxLength"/>)
        /// </remarks>
        private static readonly int s_maxVsoHashStringLength = new FileContentInfo(ContentHashingUtilities.ZeroHash, FileContentInfo.LengthAndExistence.MaxSupportedLength).Render().Length;

        /// <summary>
        /// Assumed max length of a rendered IPC moniker.
        /// </summary>
        internal static readonly int MaxIpcMonikerLength = Guid.NewGuid().ToString().Length;

        /// <summary>
        /// Function used to expand <see cref="AbsolutePath"/> to fully-qualified path as string.
        /// </summary>
        [NotNull]
        private Func<AbsolutePath, string> PathExpander { get; }

        /// <summary>
        /// String table for looking up <see cref="StringId"/>s.
        /// </summary>
        [NotNull]
        private StringTable StringTable { get; }

        /// <summary>
        /// Function used to look up <see cref="BuildXL.Cache.ContentStore.Hashing.ContentHash"/> of a given <see cref="BuildXL.Utilities.FileArtifact"/>.
        /// </summary>
        [CanBeNull]
        private ContentHashLookup HashLookup { get; }

        /// <summary>
        /// Takes a moniker ID (<see cref="BuildXL.Ipc.Interfaces.IIpcMoniker.Id"/>) and renders its value.
        /// </summary>
        [CanBeNull]
        private Func<string, string> IpcMonikerRenderer { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <remarks>
        /// Parameter <paramref name="hashLookup"/> is optional because file hashes are only known
        /// at build time, while it still makes sense to be able to render <see cref="PipData"/>
        /// at evaluation time (during pip graph construction).
        /// </remarks>
        public PipFragmentRenderer(
            Func<AbsolutePath, string> pathExpander,
            StringTable stringTable,
            Func<string, string> monikerRenderer,
            ContentHashLookup hashLookup = null)
        {
            Contract.Requires(pathExpander != null);
            Contract.Requires(stringTable != null);

            PathExpander = pathExpander;
            StringTable = stringTable;
            IpcMonikerRenderer = monikerRenderer;
            HashLookup = hashLookup;
        }

        /// <nodoc />
        public PipFragmentRenderer(PathTable pathTable)
            : this(pathTable, null, null) { }

        /// <nodoc />
        public PipFragmentRenderer(
            PathTable pathTable,
            Func<string, string> monikerRenderer,
            ContentHashLookup hashLookup)
            : this(path => path.ToString(pathTable), pathTable.StringTable, monikerRenderer, hashLookup)
        {
            Contract.Requires(pathTable != null);
        }

        /// <summary>
        /// Renders a StringId.
        /// </summary>
        public string Render(StringId stringId) => StringTable.GetString(stringId);

        /// <summary>
        /// Renders valid scalar pip data entries, i.e., those that do not correspond to <see cref="PipFragmentType.NestedFragment"/>.
        /// </summary>
        /// <remarks>
        /// If a <see cref="HashLookup"/> function is not provided, entries corresponding to <see cref="PipFragmentType.VsoHash"/>
        /// are rendered as a sequence of zeros (<see cref="s_unknownLengthFileInfoString"/>); otherwise, they are rendered as
        /// the return value of the <see cref="FileContentInfo.Render"/> method.
        /// </remarks>
        public string Render(PipFragment fragment)
        {
            Contract.Requires(fragment.FragmentType != PipFragmentType.Invalid);
            Contract.Requires(fragment.FragmentType != PipFragmentType.NestedFragment);

            switch (fragment.FragmentType)
            {
                case PipFragmentType.StringLiteral:
                    return Render(fragment.GetStringIdValue());

                case PipFragmentType.AbsolutePath:
                    return PathExpander(fragment.GetPathValue());

                case PipFragmentType.VsoHash:
                    return HashLookup != null
                        ? HashLookup(fragment.GetFileValue()).Render()
                        : s_unknownLengthFileInfoString;

                case PipFragmentType.FileId:
                {
                    var file = fragment.GetFileValue();
                    return file.Path.RawValue.ToString() + ":" + file.RewriteCount.ToString();
                }

                case PipFragmentType.IpcMoniker:
                    string monikerId = fragment.GetIpcMonikerValue().ToString(StringTable);
                    var result = IpcMonikerRenderer != null
                        ? IpcMonikerRenderer(monikerId)
                        : monikerId;
                    if (result.Length > MaxIpcMonikerLength)
                    {
                        throw new BuildXLException(I($"Moniker with id '{monikerId}' was rendered to string '{result}' which is longer than the max length for moniker fragments ({MaxIpcMonikerLength})"));
                    }

                    return result;

                default:
                    Contract.Assert(false, "Can't render fragment type " + fragment.FragmentType);
                    return null;
            }
        }

        /// <summary>
        /// Returns the length of a StringId when rendered to string using given <paramref name="escaping"/>.
        /// </summary>
        public int GetLength(StringId stringId, PipDataFragmentEscaping escaping)
        {
            switch (escaping)
            {
                case PipDataFragmentEscaping.CRuntimeArgumentRules:
                    // - passing null as StringBuilder to AppendEscapedCommandLineWord is ok
                    // - the return value of AppendEscapedCommandLineWord indicates how many characters would be appended
                    return CommandLineEscaping.AppendEscapedCommandLineWord(builder: null, word: Render(stringId));

                case PipDataFragmentEscaping.NoEscaping:
                    return StringTable.GetLength(stringId); // instead of retrieving the whole string, StringTable.GetLength is faster.

                default:
                    Contract.Assert(false, I($"Unhandled fragmentEscaping: {escaping}"));
                    return 0;
            }
        }

        /// <summary>
        /// Returns max possible length of given <paramref name="fragment"/>, when <paramref name="escaping"/>
        /// is used.  Instead of rendering paths and computing actual path lengths, it uses
        /// <paramref name="maxPathLength"/> as the upper bound for path lengths.
        /// </summary>
        public int GetMaxLength(PipFragment fragment, PipDataFragmentEscaping escaping, int maxPathLength)
        {
            Contract.Requires(maxPathLength > 0);

            // StringLiteral
            if (fragment.FragmentType == PipFragmentType.StringLiteral)
            {
                return GetLength(fragment.GetStringIdValue(), escaping);
            }

            // AbsolutePath
            if (fragment.FragmentType == PipFragmentType.AbsolutePath)
            {
                var numExtraCharactersForEscaping =
                    escaping == PipDataFragmentEscaping.CRuntimeArgumentRules ? 2 : // path gets surrounded by '"' and '"'
                    0;
                return maxPathLength + numExtraCharactersForEscaping;
            }

            // VsoHash
            if (fragment.FragmentType == PipFragmentType.VsoHash)
            {
                return s_maxVsoHashStringLength; // vso hash should never need any escaping
            }

            // File id.
            if (fragment.FragmentType == PipFragmentType.FileId)
            {
                return (2 * int.MaxValue.ToString().Length) + 1;
            }

            if (fragment.FragmentType == PipFragmentType.IpcMoniker)
            {
                return MaxIpcMonikerLength;
            }

            Contract.Assert(false, I($"Unhandled fragment ('{fragment.FragmentType}') type and/or fragmentEscaping ('{escaping}')"));
            return 0;
        }
    }
}
