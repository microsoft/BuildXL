// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using TypeScript.Net.TypeChecking;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// p``, d`` or f`` literal with a converted <see cref="AbsolutePath"/> value.
    /// </summary>
    public sealed partial class AbsolutePathLiteralExpression : PathLikeLiteral
    {
        /// <inheritdoc />
        public override int Id => (int)WellKnownNodeIds.AbsolutePathLiteralId;

        /// <nodoc />
        public AbsolutePath Path
        {
            get { return new AbsolutePath(m_reservedInt); }
            internal set { m_reservedInt = value.RawValue; }
        }

        /// <inheritdoc />
        protected override string GetText() => Path.ToString(PathTable, PathFormat.Script);
    }
}
