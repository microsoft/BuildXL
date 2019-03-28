// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
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
