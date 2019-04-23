// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Declarations
{
    /// <summary>
    /// Declaration node.
    /// </summary>
    public abstract class Declaration : Node
    {
        /// <summary>
        /// Modifier.
        /// </summary>
        [Flags]
        public enum DeclarationFlags : byte
        {
            /// <summary>
            /// No Modifier
            /// </summary>
            None = 0x0,

            /// <summary>
            /// Export Modifier
            /// </summary>
            Export = 0x1,

            /// <summary>
            /// Ambient Modifier
            /// </summary>
            Ambient = 0x2,
        }

        /// <summary>
        /// Modifier.
        /// </summary>
        public DeclarationFlags Modifier { get; }

        /// <nodoc />
        protected Declaration(DeclarationFlags modifier, LineInfo location)
            : base(location)
        {
            Modifier = modifier;
        }

        /// <nodoc />
        protected Declaration(DeserializationContext context, LineInfo location)
            : base(location)
        {
            Modifier = ReadModifier(context.Reader);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            WriteModifier(Modifier, writer);
        }

        /// <summary>
        /// Gets modifier string.
        /// </summary>
        /// <remarks>An explicit export is not reflected in the modifier string, since this is used for pretty printing purposes</remarks>
        internal string GetModifierString()
        {
            if ((Modifier & DeclarationFlags.Export) != 0)
            {
                return "export ";
            }

            if ((Modifier & DeclarationFlags.Ambient) != 0)
            {
                return "declare ";
            }

            return string.Empty;
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            throw new NotImplementedException();
        }
    }
}
