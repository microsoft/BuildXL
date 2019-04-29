// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Array literal instance constructed at ast conversion time.
    /// </summary>
    /// <remarks>
    /// Original implemnetation of array literals was based on the array of objects. This allowed to use the same type for both - an array of expressions and the array of results.
    /// With the switch to <see cref="EvaluationResult"/> everywhere the old approach is no longer possible. Now we have to separate original (unevaluated) array from the evaluated array that has only values.
    /// This separations lead to a more clear design and opens possibilities for further optimizations. For instance, we may create and instance of evaluated array during ast conversion if we know that all the elements are constants.
    /// </remarks>
    public sealed class UnevaluatedArrayLiteral : ArrayLiteral
    {
        private readonly Expression[] m_data;

        internal UnevaluatedArrayLiteral(Expression[] elements, LineInfo location, AbsolutePath path)
            : base(location, path)
        {
            m_data = elements;
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Path);
            writer.WriteCompact(Length);
            
            // Unevaluated array
            writer.Write(false);

            for (int i = 0; i < Length; i++)
            {
                Expression node = m_data[i];
                node.Serialize(writer);
            }
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            if (Length == 0)
            {
                if (context.TrackMethodInvocationStatistics)
                {
                    Interlocked.Increment(ref context.Statistics.EmptyArrays);
                }

                return EvaluationResult.Create(this);
            }

            return EvaluateSynchronously(context, env, frame);
        }

        /// <inheritdoc />
        public override int Count => m_data.Length;

        // This property is used only by the pretty printer and never used by the product code.
        // Product code gets only evaluated values via EvaluatedArrayLiteral instance.
        /// <inheritdoc />
        public override IReadOnlyList<EvaluationResult> Values => m_data.Select(v => EvaluationResult.Create(v)).ToArray();

        /// <inheritdoc />
        public override int Length => m_data.Length;

        /// <inheritdoc />
        public override EvaluationResult this[int index] => EvaluationResult.Create(m_data[index]);

        /// <inheritdoc />
        public override void Copy(int sourceIndex, EvaluationResult[] destination, int destinationIndex, int length)
        {
            throw new NotImplementedException();
        }

        private EvaluationResult EvaluateSynchronously(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Contract.Requires(Length > 0);

            if (context.TrackMethodInvocationStatistics)
            {
                Interlocked.Increment(ref context.Statistics.ArrayEvaluations);
            }

            var results = new EvaluationResult[Length];
            bool error = false;
            for (var i = 0; i < Length; ++i)
            {
                if (m_data[i] is Expression e)
                {
                    var itemResult = e.Eval(context, env, args);
                    error |= itemResult.IsErrorValue;
                    // Today we evaluated all the values of the array regardless of the result.
                    // This is the reminiscent of all logic when the arrays supported parallel evaluation.
                    results[i] = itemResult;
                }
                else
                {
                    results[i] = EvaluationResult.Create(m_data[i]);
                }
            }

            return error ? EvaluationResult.Error : EvaluationResult.Create(CreateWithoutCopy(results, Location, Path));
        }
    }
}
