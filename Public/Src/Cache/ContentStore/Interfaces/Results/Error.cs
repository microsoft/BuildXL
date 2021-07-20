// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Text;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Represents an error value of a result.
    /// </summary>
    /// <remarks>
    /// This is the union type: string ErrorMessage + (Exception | string diagnostics)?
    /// When the error is merged from two errors, then both <see cref="Exception"/> and <see cref="Diagnostics"/> properties can be not-null.
    /// </remarks>
    public sealed class Error : IEquatable<Error>
    {
        private bool _hasLazyDiagnostics;
        private string? _diagnostics;

        /// <summary>
        /// Global preprocessor for error text for result errors. This allows for the exception string demystification code
        /// to be injected dynamically without requiring a dependency on BuildXL.Utilities. Generally adding new dependencies
        /// to this assembly is an involved process so this is here as a workaround.
        /// </summary>
        public static Func<Exception, string>? ExceptionToTextConverter { get; set; }
        
        /// <summary>
        /// A message that describe an error.
        /// </summary>
        public string ErrorMessage { get; }

        /// <nodoc />
        [MemberNotNullWhen(true, nameof(Exception))]
        public bool HasException => Exception != null;

        /// <summary>
        /// An optional exception instance that happen during an operation.
        /// </summary>
        public Exception? Exception { get; }

        /// <summary>
        /// An optional diagnostics message of the error.
        /// </summary>
        /// <remarks>
        /// The message can be constructed on the fly from the <see cref="Exception"/> if the <see cref="Exception"/> is not null.
        /// </remarks>
        public string? Diagnostics
        {
            get
            {
                if (Exception != null && _hasLazyDiagnostics)
                {
                    _diagnostics = CreateDiagnostics(Exception);
                }

                return _diagnostics;
            }
        }

        private Error(string errorMessage, Exception? exception, string? diagnostics)
        {
            (ErrorMessage, Exception, _diagnostics) = (NotNullOrEmpty(errorMessage), exception, diagnostics);
            // The diagnostics message is constructed from the exception instance only when the diagnostics are not provided.
            _hasLazyDiagnostics = diagnostics == null;
        }

        /// <summary>
        /// Creates an error from with a given <paramref name="errorMessage"/> and <paramref name="exception"/>.
        /// </summary>
        public static Error FromException(Exception exception, string? errorMessage) => new Error(GetErrorMessageFromException(errorMessage, exception), exception, diagnostics: null);

        /// <summary>
        /// Creates an error with a given <paramref name="errorMessage"/> and optional <paramref name="diagnostics"/>.
        /// </summary>
        public static Error FromErrorMessage(string errorMessage, string? diagnostics = null) => new Error(errorMessage, exception: null, diagnostics);

        /// <summary>
        /// Merges two error instances into one.
        /// </summary>
        public static Error? Merge(Error? left, Error? right, string separator)
        {
            if (left is null && right is null)
            {
                return null;
            }

            if (left is null)
            {
                return right;
            }

            if (right is null)
            {
                return left;
            }

            var message = ResultBase.Merge(left.ErrorMessage, right.ErrorMessage, separator);
            if (left.Exception != null && right.Exception != null)
            {
                var exception = new AggregateException(left.Exception, right.Exception);
                return new Error(message, exception, diagnostics: null);
            }

            var diagnostics = ResultBase.Merge(left.Diagnostics, right.Diagnostics, separator);

            // Keeping an exception instance for the error (this is important for tracking critical errors for instance).
            return new Error(message, exception: left.Exception ?? right.Exception, diagnostics);
        }

        /// <nodoc />
        public override int GetHashCode()
        {
            return (ErrorMessage, Exception, _diagnostics).GetHashCode();
        }

        /// <inheritdoc />
        public bool Equals(Error? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return (ErrorMessage, Exception, _diagnostics).Equals((other.ErrorMessage, other.Exception, other._diagnostics));
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as Error);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"Error=[{ErrorMessage}]");

            if (Exception != null && _hasLazyDiagnostics)
            {
                // This is the case when both 'diagnostics' and 'exception' are provided.
                // Need to trace them both.
                sb.Append($", Exception=[{CreateDiagnostics(Exception)}]");
            }
            else if (Diagnostics != null)
            {
                sb.Append($", Diagnostics=[{Diagnostics}]");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Returns true if the error is critical.
        /// </summary>
        public bool IsCritical(bool isCanceled) => isCanceled ? Result.IsCriticalForCancellation(Exception) : Result.IsCritical(Exception);

        private static string NotNullOrEmpty(string str)
        {
            Contract.Requires(!string.IsNullOrEmpty(str));
            return str;
        }

        private static string GetErrorMessageFromException(string? message, Exception exception)
        {
            // NOTE: The use of Exception.Message is intentional here as
            // the full exception string is captured in the ResultBase.Diagnostics
            // property
            return string.IsNullOrEmpty(message)
                ? $"{exception.GetType().Name}: {GetErrorMessageFromException(exception)}"
                : $"{message}: {exception.GetType().Name}: {GetErrorMessageFromException(exception)}";
        }

        private static string CreateDiagnostics(Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                var sb = new StringBuilder();
                var i = 0;

                foreach (var e in aggregateException.Flatten().InnerExceptions)
                {
                    if (i > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.AppendLine("Exception #" + i);
                    sb.Append(GetExceptionString(e));
                    i++;
                }

                return sb.ToString();
            }

            return GetExceptionString(exception);
        }

        /// <summary>
        /// Gets a preprocessed equivalent of <see cref="System.Exception.ToString"/> if <see cref="ExceptionToTextConverter"/> is specified. Otherwise,
        /// just returns <see cref="System.Exception.ToString"/>.
        /// </summary>
        internal static string GetExceptionString(Exception ex)
        {
            var processErrorText = ExceptionToTextConverter;
            if (processErrorText != null)
            {
                return processErrorText(ex);
            }

            return ex.ToString();
        }

        /// <summary>
        /// Gets full log event message including inner exceptions from the given exception
        /// </summary>
        private static string GetErrorMessageFromException(Exception exception)
        {
            return string.Join(": " + Environment.NewLine, getExceptionMessages());

            IEnumerable<string> getExceptionMessages()
            {
                for (Exception? currentException = exception; currentException != null; currentException = currentException.InnerException)
                {
                    yield return currentException.Message;
                }
            }
        }

        /// <summary>
        /// Create another error instance from the current one with extra <paramref name="message"/>.
        /// </summary>
        public Error WithExtraMessage(string? message)
        {
            return string.IsNullOrEmpty(message)
                ? this
                : new Error(errorMessage: $"{message}: {ErrorMessage}", exception: Exception, diagnostics: Diagnostics);
        }
    }
}
