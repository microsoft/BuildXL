// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using TypeScript.Net.TypeChecking;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// a`` literal with a converted <see cref="PathAtom"/> value.
    /// </summary>
    public sealed partial class PathAtomLiteralExpression : PathLikeLiteral
    {
        /// <inheritdoc />
        public override int Id => (int)WellKnownNodeIds.PathAtomLiteralId;

        /// <nodoc />
        public PathAtom Atom
        {
            get { return PathAtom.UnsafeCreateFrom(StringId.UnsafeCreateFrom(m_reservedInt)); }
            internal set { m_reservedInt = value.StringId.Value; }
        }

        /// <inheritdoc />
        protected override string GetText() => Atom.ToString(PathTable.StringTable);
    }
}
