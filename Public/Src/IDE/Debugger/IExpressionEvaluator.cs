// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// Responsible for evaluating expressions in 'immediate' mode
    /// </summary>
    public interface IExpressionEvaluator
    {
        /// <summary>
        /// Evaluates an expression in the current debugger state context
        /// </summary>
        Possible<ObjectContext, Failure> EvaluateExpression(ThreadState threadState, int frameIndex, string expressionString, bool evaluateForCompletions);
    }
}
