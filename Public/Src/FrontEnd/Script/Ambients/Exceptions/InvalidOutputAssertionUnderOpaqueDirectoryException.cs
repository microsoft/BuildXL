// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Exception that is thrown when an invalid output file is asserted to exist under an opaque directory
    /// </summary>
    public sealed class InvalidOutputAssertionUnderOpaqueDirectoryException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        public InvalidOutputAssertionUnderOpaqueDirectoryException(string absolutePathToInvalidFile, ErrorContext errorContext)
            : base (
               I($"Invalid file '{absolutePathToInvalidFile}' asserted to exist under opaque directory."),
               errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(absolutePathToInvalidFile));
            AbsolutePathInvalidFile = absolutePathToInvalidFile;
        }

        /// <summary>
        /// Path to an invalid output assertion under an opaque directory
        /// </summary>
        public string AbsolutePathInvalidFile { get; }

        /// <inheritdoc/>
        public override void ReportError(
            EvaluationErrors errors,
            ModuleLiteral environment,
            LineInfo location,
            Expression expression,
            Context context)
        {
            errors.ReportFileNotFoundInStaticDirectory(environment, AbsolutePathInvalidFile, location);
        }
    }
}
