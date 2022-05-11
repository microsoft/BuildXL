// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Represents an error value of a result.
    /// </summary>
    /// <remarks>
    /// This is the union type: string ErrorMessage + (Exception | string diagnostics)?
    /// When the error is merged from two errors, then both <see cref="Exception"/> and <see cref="Diagnostics"/> properties can be not-null.
    ///
    /// If the instance is created from <see cref="Exception"/> then <see cref="ErrorMessage"/> property contains an exception's message(s) and
    /// <see cref="Diagnostics"/> property contains a stack trace.
    /// </remarks>
    public sealed class Error : IEquatable<Error>
    {
        private readonly Lazy<string>? _diagnostics;

        /// <summary>
        /// A global preprocessor for error text for result errors. This allows for the exception string demystification code
        /// to be injected dynamically without requiring a dependency on BuildXL.Utilities. Generally adding new dependencies
        /// to this assembly is an involved process so this is here as a workaround.
        /// </summary>
        public static Func<Exception, string>? ExceptionToTextConverter { get; set; }

        /// <summary>
        /// A helper function (similar to <see cref="ExceptionToTextConverter"/> that gets an exception's StackTrace in a pretty form.
        /// </summary>
        public static Func<Exception, string?> ExceptionStackTraceToTextConverter { get; set; } = static ex => ex.StackTrace;

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
        public string? Diagnostics => _diagnostics?.Value;

        private Error(string errorMessage, Exception? exception, Lazy<string>? lazyDiagnostics)
        {
            Contract.Requires(!string.IsNullOrEmpty(errorMessage));

            (ErrorMessage, Exception, _diagnostics) = (errorMessage, exception, lazyDiagnostics);
        }

        private Error(string errorMessage, Exception? exception, string? diagnostics)
            : this(errorMessage, exception, string.IsNullOrEmpty(diagnostics) ? null : new Lazy<string>(() => diagnostics!))
        {
            
        }

        /// <summary>
        /// Creates an error from with a given <paramref name="errorMessage"/> and <paramref name="exception"/>.
        /// </summary>
        public static Error FromException(Exception exception, string? errorMessage = null)
        {
            var message = exception is ResultPropagationException rpe ? rpe.Message : GetErrorMessageFromException(errorMessage, exception);
            var diagnostics = new Lazy<string>(() => CreateDiagnostics(exception));
            return new Error(message, exception, diagnostics);
        }

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
                return new Error(message, exception, lazyDiagnostics: null);
            }

            var diagnostics = ResultBase.Merge(left.Diagnostics, right.Diagnostics, separator);

            // Keeping an exception instance for the error (this is important for tracking critical errors for instance).
            return new Error(message, exception: left.Exception ?? right.Exception, diagnostics);
        }

        /// <nodoc />
        public override int GetHashCode()
        {
            return (ErrorMessage, Exception, Diagnostics).GetHashCode();
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

            return (ErrorMessage, Exception, Diagnostics).Equals((other.ErrorMessage, other.Exception, other.Diagnostics));
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

            if (!string.IsNullOrEmpty(Diagnostics))
            {
                sb.Append($", Diagnostics=[{Diagnostics}]");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Returns true if the error is critical.
        /// </summary>
        public bool IsCritical(bool isCanceled) => isCanceled ? ResultBase.IsCriticalForCancellation(Exception) : ResultBase.IsCritical(Exception);

        private static string GetErrorMessageFromException(string? message, Exception exception)
        {
            // ErrorMessage contains only all the messages (included all the nested once)
            return string.IsNullOrEmpty(message)
                ? GetErrorMessageFromException(exception)
                : $"{message}: {GetErrorMessageFromException(exception)}";
        }

        private static string CreateDiagnostics(Exception exception)
        {
            // Getting an enhanced stack trace for the current exception as well as all the nested once.
            var sb = new StringBuilder();

            sb.AppendLine("StackTrace: " + ExceptionStackTraceToTextConverter(exception));
            foreach (var inner in EnumerateInnerExceptionsAndSelf(exception).Skip(1))
            {
                sb.AppendLine($"{inner.GetType().Name}: {ExceptionStackTraceToTextConverter(inner)}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets a preprocessed equivalent of <see cref="System.Exception.ToString"/> if <see cref="ExceptionToTextConverter"/> is specified. Otherwise,
        /// just returns <see cref="System.Exception.ToString"/>.
        /// </summary>
        internal static string GetExceptionString(Exception ex)
        {
            return ExceptionToTextConverter?.Invoke(ex) ?? ex.ToString();
        }

        /// <summary>
        /// Gets full log event message including inner exceptions from the given exception.
        /// </summary>
        private static string GetErrorMessageFromException(Exception exception)
        {
            // The logic is very similar to one used AggregateException.Message implementation,
            // the only difference is that this version also prints the type of an exception as well.
            var sb = new StringBuilder();

            // Filtering out 'ResultPropagationException' because they don't add useful information in terms of messages.
            foreach (var inner in EnumerateInnerExceptionsAndSelf(exception).Where(e => e is not ResultPropagationException))
            {
                sb.Append($"({inner.GetType().Name}: {inner.Message}) ");
            }

            // Removing an extra space added by the first or the nested Append calls.
            sb.Length--;
            return sb.ToString();
        }

        private static IEnumerable<Exception> EnumerateInnerExceptionsAndSelf(Exception? exception)
        {
            if (exception is null)
            {
                yield break;
            }

            yield return exception;

            if (exception is AggregateException ae)
            {
                foreach (var e in ae.InnerExceptions.SelectMany(static e => EnumerateInnerExceptionsAndSelf(e)))
                {
                    yield return e;
                }
            }

            foreach (var inner in EnumerateInnerExceptionsAndSelf(exception.InnerException))
            {
                yield return inner;
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
