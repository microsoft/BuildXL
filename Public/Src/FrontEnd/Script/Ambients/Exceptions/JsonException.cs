// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Ambients.Exceptions
{
    /// <nodoc />
    public sealed class JsonUnsuportedTypeForSerializationException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public JsonUnsuportedTypeForSerializationException(string encounteredType, ErrorContext errorContext)
            : base("Unsupported type for json writing", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(encounteredType));

            EncounteredType = encounteredType;
        }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public string EncounteredType { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportJsonUnsuportedTypeForSerialization(environment, this, location);
        }
    }

    /// <nodoc />
    public sealed class JsonUnsuportedDynamicFieldsForSerializationException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public JsonUnsuportedDynamicFieldsForSerializationException(string encounteredType, string expectedType, ErrorContext errorContext)
            : base("Unsupported type for json writing", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(encounteredType));

            EncounteredType = encounteredType;
            ExpectedType = expectedType;
        }

        /// <summary>
        /// Gets the encountered type.
        /// </summary>
        public string EncounteredType { get; }

        /// <summary>
        /// Gets the expected type.
        /// </summary>
        public string ExpectedType { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportJsonUnsuportedDynamicFieldsForSerialization(environment, this, location);
        }
    }
}
