// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception that occurs when ambient global fails to coerce qualifiers.
    /// </summary>
    public sealed class QualifierCoerceException : EvaluationException
    {
        /// <nodoc />
        public QualifierId QualifierId { get; }

        /// <nodoc />
        public QualifierSpaceId QualifierSpaceId { get; }

        /// <nodoc />
        public QualifierCoerceException(QualifierId qualifierId, QualifierSpaceId qualifierSpaceId)
        {
            Contract.Requires(qualifierId.IsValid);
            Contract.Requires(qualifierSpaceId.IsValid);

            QualifierId = qualifierId;
            QualifierSpaceId = qualifierSpaceId;
        }

        /// <inheritdoc/>
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportQualifierCannotBeCoarcedToQualifierSpace(environment, QualifierId, QualifierSpaceId, location);
        }
    }
}
