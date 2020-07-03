// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception that occurs when an ambient produces assertion violation.
    /// </summary>
    public sealed class ContractRequireException : EvaluationException
    {
        /// <nodoc />
        public ContractRequireException(string message)
            : base(message)
        {
            Contract.Requires(message != null);
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportContractRequire(environment, expression, Message, location);
        }
    }
}
