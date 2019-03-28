// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Exception that occurs if argument index is larger than the length of an indexed string.
    /// </summary>
    public sealed class StringIndexOutOfBoundException : EvaluationException
    {
        /// <summary>
        /// Requested index.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Indexed string.
        /// </summary>
        public string Target { get; }

        /// <nodoc />
        public StringIndexOutOfBoundException(int index, string target)
        {
            Contract.Requires(target != null);
            Contract.Requires(index < 0 || index >= target.Length);

            Index = index;
            Target = target;
        }

        /// <inheritdoc/>
        public override void ReportError(
            EvaluationErrors errors,
            ModuleLiteral environment,
            LineInfo location,
            Expression expression,
            Context context)
        {
            errors.ReportStringIndexOufOfRange(environment, Index, Target, location);
        }
    }
}
