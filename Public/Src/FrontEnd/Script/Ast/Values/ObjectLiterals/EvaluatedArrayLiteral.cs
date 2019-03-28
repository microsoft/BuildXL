// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Array literal that was created during evaluation of another array literal.
    /// </summary>
    public class EvaluatedArrayLiteral : ArrayLiteral
    {
        private readonly EvaluationResult[] m_data;

        internal EvaluatedArrayLiteral(EvaluationResult[] data, LineInfo location, AbsolutePath path)
            : base(location, path)
        {
            m_data = data;
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Path);
            writer.WriteCompact(Length);
            
            // Evaluated array
            writer.Write(true);

            // This should be an array of constant expressions.
            for (int i = 0; i < Length; i++)
            {
                ConstExpressionSerializer.WriteConstValue(writer, m_data[i].Value);
            }
        }

        /// <inheritdoc />
        public override IReadOnlyList<EvaluationResult> Values => m_data;

        /// <inheritdoc />
        public override int Length => m_data.Length;

        /// <inheritdoc />
        public override int Count => m_data.Length;

        /// <inheritdoc />
        public override EvaluationResult this[int index] => m_data[index];

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            if (context.TrackMethodInvocationStatistics)
            {
                Interlocked.Increment(ref context.Statistics.AlreadyEvaluatedArrays);
            }

            return EvaluationResult.Create(this);
        }

        /// <inheritdoc />
        public override void Copy(int sourceIndex, EvaluationResult[] destination, int destinationIndex, int length)
        {
            Array.Copy(m_data, sourceIndex, destination, destinationIndex, length);
        }
    }
}
