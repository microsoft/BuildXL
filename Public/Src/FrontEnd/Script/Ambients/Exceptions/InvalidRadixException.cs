// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception that occurs when radix is not 2, 8, 10 or 16 specified in <code>number.ToString(radix)</code>.
    /// </summary>
    public sealed class InvalidRadixException : EvaluationException
    {
        // Actual radix
        private readonly int m_radix;

        /// <nodoc />
        public InvalidRadixException(int radix)
        {
            m_radix = radix;
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportInvalidRadix(environment, expression, location, m_radix);
        }
    }
}
