// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using TypeScript.Net.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Ambients.Exceptions
{
    /// <nodoc />
    public sealed class KeyFormDllNotFoundException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public KeyFormDllNotFoundException(string keyFormDllPath, ErrorContext errorContext)
            : base("Could not find keyForm dll", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(keyFormDllPath));

            KeyFormDllPath = keyFormDllPath;
        }

        /// <summary>
        /// Gets the target type.
        /// </summary>
        public string KeyFormDllPath { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportKeyFormDllNotFound(environment, this, location);
        }
    }

    /// <nodoc />
    public sealed class KeyFormDllWrongFileNameException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public KeyFormDllWrongFileNameException(string keyFormDllPath, string expectedKeyFormFileName, ErrorContext errorContext)
            : base("Specified KeyForm filename has wrong file name", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(keyFormDllPath));
            Contract.Requires(!string.IsNullOrEmpty(expectedKeyFormFileName));

            KeyFormDllPath = keyFormDllPath;
            ExpectedKeyFormFileName = expectedKeyFormFileName;
        }

        /// <nodoc />
        public string KeyFormDllPath { get; }

        /// <nodoc />
        public string ExpectedKeyFormFileName { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportKeyFormDllWrongFileName(environment, this, location);
        }
    }

    /// <nodoc />
    public sealed class KeyFormDllLoadException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public KeyFormDllLoadException(string keyFormDllPath, int lastError, string errorMessage, ErrorContext errorContext)
            : base("Specified KeyForm file not found", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(keyFormDllPath));

            KeyFormDllPath = keyFormDllPath;
            LastError = lastError;
            ErrorMessage = errorMessage;
        }

        /// <nodoc />
        public string KeyFormDllPath { get; }

        /// <nodoc />
        public int LastError { get; }

        /// <nodoc />
        public string ErrorMessage { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportKeyFormDllLoad(environment, this, location);
        }
    }

    /// <nodoc />
    public sealed class KeyFormDllLoadedWithDifferentDllException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public KeyFormDllLoadedWithDifferentDllException(string keyFormDllPath, string otherKeyFormDllPath, ErrorContext errorContext)
            : base("Specified KeyForm already loaded from a different file", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(keyFormDllPath));

            KeyFormDllPath = keyFormDllPath;
            OtherKeyFormDllPath = otherKeyFormDllPath;
        }

        /// <nodoc />
        public string KeyFormDllPath { get; }

        /// <nodoc />
        public string OtherKeyFormDllPath { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportKeyFormDllLoadedWithDifferentDll(environment, this, location);
        }
    }

    /// <nodoc />
    public sealed class KeyFormNativeFailureException : EvaluationExceptionWithErrorContext
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA2204:LiteralsShouldBeSpelledCorrectly")]
        public KeyFormNativeFailureException(string keyFormDllPath, Exception exception, ErrorContext errorContext)
            : base("Specified KeyForm file failed to load, either not a dll, or wrong architecture", errorContext)
        {
            Contract.Requires(!string.IsNullOrEmpty(keyFormDllPath));
            Contract.Requires(exception != null);

            KeyFormDllPath = keyFormDllPath;
            Exception = exception;
        }

        /// <nodoc />
        public string KeyFormDllPath { get; }

        /// <nodoc />
        public Exception Exception { get; }

        /// <inheritdoc />
        public override void ReportError(EvaluationErrors errors, ModuleLiteral environment, LineInfo location, Expression expression, Context context)
        {
            errors.ReportKeyFormNativeFailure(environment, this, location);
        }
    }
}
