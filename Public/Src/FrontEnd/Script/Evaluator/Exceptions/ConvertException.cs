// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Exception that occurs on conversion of objects to values of specific types.
    /// </summary>
    public sealed class ConvertException : EvaluationExceptionWithErrorContext
    {
        /// <summary>
        /// Function that computes a string representation of the expected types.
        /// </summary>
        [SuppressMessage("Microsoft.Security", "CA2105:ArrayFieldsShouldNotBeReadOnly")]
        public readonly Func<ImmutableContextBase, string> ExpectedTypesToString;

        /// <summary>
        /// The given value.
        /// </summary>
        public EvaluationResult Value { get; }

        /// <nodoc />
        public ConvertException(Type[] expectedTypes, EvaluationResult value, ErrorContext errorContext, string errorDescription = null)
            : base(CreateDebugMessage(expectedTypes, value, errorDescription), errorContext)
        {
            Contract.Requires(expectedTypes != null);
            Contract.Requires(expectedTypes.Length > 0);

            ExpectedTypesToString = (context => DisplayStringHelper.UnionTypeToString(context, expectedTypes));
            Value = value;
        }

        /// <nodoc />
        public ConvertException(string expectedTypesErrorMessage, EvaluationResult value, ErrorContext errorContext, string errorDescription = null)
            : base(CreateDebugMessage(expectedTypesErrorMessage, value, errorDescription), errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(expectedTypesErrorMessage));

            ExpectedTypesToString = (context => expectedTypesErrorMessage);
            Value = value;
        }

        private static string CreateDebugMessage(Type[] expectedTypes, EvaluationResult value, string errorDescription = null)
        {
            return CreateDebugMessage(string.Join(" | ", expectedTypes.Select(t => t.Name)), value, errorDescription);
        }

        private static string CreateDebugMessage(string expectedTypes, EvaluationResult value, string errorDescription = null)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}Failed to convert instance of type '{1}'. Expected types are {2}.",
                errorDescription != null ? errorDescription + " " : string.Empty,
                value.Value?.GetType().ToString() ?? "null", 
                expectedTypes);
        }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            throw new NotSupportedException("ConversionException doesn't support ReportError.");
        }
    }
}
