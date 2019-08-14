// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        Possible<ObjectContext, Failure> EvaluateExpression(ThreadState threadState, int frameIndex, string expressionString);
    }
}
