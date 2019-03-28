// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Exception that occurs if argument index is larger than the length of the given arguments.
    /// </summary>
    public sealed class ArgumentIndexOutOfBoundException : EvaluationException
    {
        /// <summary>
        /// Requested index.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Number of arguments passed on calling the ambient.
        /// </summary>
        public int NumberOfArguments { get; }

        /// <nodoc />
        public ArgumentIndexOutOfBoundException(int index, int numberOfArguments)
        {
            Contract.Requires(index >= 0);
            Contract.Requires(numberOfArguments >= 0);

            Index = index;
            NumberOfArguments = numberOfArguments;
        }

        /// <inheritdoc/>
        public override void ReportError(
            EvaluationErrors errors,
            ModuleLiteral environment,
            LineInfo location,
            Expression expression,
            Context context)
        {
            errors.ReportArgumentIndexOutOfBound(environment, Index, NumberOfArguments, location);
        }
    }
}
