// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Evaluation statistics
    /// </summary>
    public sealed class EvaluationStatistics
    {
        /// <summary>
        /// How many method invocations happened during evaluation.
        /// </summary>
        public FunctionInvocationStatistics FunctionInvocationStatistics { get; } = new FunctionInvocationStatistics();

        /// <summary>
        /// How many evaluation context trees were created
        /// </summary>
        public long ContextTrees;

        /// <summary>
        /// How many evaluation context were created
        /// </summary>
        public long Contexts;

        /// <summary>
        /// How many array evaluations had zero elements
        /// </summary>
        public long EmptyArrays;
        
        /// <summary>
        /// How many array evlauations were skipped because the constructed array consists of consts.
        /// </summary>
        public long AlreadyEvaluatedArrays;

        /// <summary>
        /// How many array evaluations were executed synchronously
        /// </summary>
        public long ArrayEvaluations;

        /// <summary>
        /// Total glob time in ticks.
        /// </summary>
        public long TotalGlobTimeInTicks;
    }
}
