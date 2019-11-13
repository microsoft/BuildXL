// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients.Exceptions
{
    /// <summary>
    /// Exception that occurs due to a call to an unsafe ambient.
    /// </summary>
    public sealed class DisallowedUnsafeAmbientCallException : EvaluationException
    {
        /// <summary>
        /// Method name.
        /// </summary>
        public string MethodName { get; }

        /// <nodoc />
        public DisallowedUnsafeAmbientCallException(string methodName)
        {
            Contract.Requires(!string.IsNullOrEmpty(methodName));
            MethodName = methodName;
        }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportDisallowedUnsafeAmbientCallError(environment, expression, MethodName, location);
        }
    }
}
