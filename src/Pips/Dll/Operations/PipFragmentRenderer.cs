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
        /// Takes a moniker ID and renders its value.
        /// </summary>
        [CanBeNull]
        private Func<string, string> IpcMonikerRenderer { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public PipFragmentRenderer(
            Func<AbsolutePath, string> pathExpander,
            StringTable stringTable,
            Func<string, string> monikerRenderer)
        {
            Contract.Requires(pathExpander != null);
            Contract.Requires(stringTable != null);

            PathExpander = pathExpander;
            StringTable = stringTable;
            IpcMonikerRenderer = monikerRenderer;
        }

        /// <nodoc />
        public PipFragmentRenderer(PathTable pathTable)
            : this(pathTable, null) { }

        /// <nodoc />
        public PipFragmentRenderer(
            PathTable pathTable,
            Func<string, string> monikerRenderer)
            : this(path => path.ToString(pathTable), pathTable.StringTable, monikerRenderer)
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

            Contract.Assert(false, I($"Unhandled fragment ('{fragment.FragmentType}') type and/or fragmentEscaping ('{escaping}')"));
            return 0;
        }
    }
}
