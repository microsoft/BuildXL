// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Represents the result of some possibly-successful function.
    /// </summary>
    public class ResultBase
    {
        /// <summary>
        /// Global preprocessor for error text for result errors. This allows for the exception string demystification code
        /// to be injected dynamically without requiring a dependency on BuildXL.Utilities. Generally adding new dependencies
        /// to this assembly is an involved process so this is here as a workaround.
        /// </summary>
        internal static Func<Exception, string> ResultExceptionTextProcessor { get; set; }

        /// <summary>
        /// Constructor for creating successful result instances.
        /// </summary>
        protected ResultBase()
        {
        }

        /// <summary>
        /// Creates a new instance of a failed result if <paramref name="errorMessage"/> is not null or empty.
        /// </summary>
        protected ResultBase(string errorMessage, string diagnostics)
        {
            ErrorMessage = errorMessage;
            Diagnostics = diagnostics;
        }

        /// <summary>
        /// Creates a new instance of a result that failed with a given exception.
        /// </summary>
        protected ResultBase(Exception exception, string message = null)
        {
            Contract.Requires(exception != null);

            if (exception is ResultPropagationException other)
            {
                ErrorMessage = string.IsNullOrEmpty(message)
                    ? other.Result.ErrorMessage
                    : $"{message}: {other.Result.ErrorMessage}";
                _hasLazyDiagnostics = other.Result._hasLazyDiagnostics;
                _diagnostics = other.Result._diagnostics;
                IsCancelled = other.Result.IsCancelled;
                Exception = other.InnerException;
            }
            else
            {
                // NOTE: The use of Exception.Message is intentional here as
                // the full exception string is captured in the ResultBase.Diagnostics
                // property
                ErrorMessage = string.IsNullOrEmpty(message)
                    ? $"{exception.GetType().Name}: {GetErrorMessageFromException(exception)}"
                    : $"{message}: {exception.GetType().Name}: {GetErrorMessageFromException(exception)}";

                // Indicate that diagnostics should lazily be computed from exception.
                _hasLazyDiagnostics = true;
                Exception = exception;
            }
        }

        /// <summary>
        /// Creates a new instance of the result from another result instance.
        /// </summary>
        protected ResultBase(ResultBase other, string message)
        {
            Contract.Requires(other != null);

            ErrorMessage = string.IsNullOrEmpty(message)
                ? other.ErrorMessage
                : $"{message}: {other.ErrorMessage}";
            _diagnostics = other._diagnostics;
            _hasLazyDiagnostics = other._hasLazyDiagnostics;
            Exception = other.Exception;
            IsCancelled = other.IsCancelled;
        }

        /// <summary>
        /// Indicates if this is a successful outcome or not.
        /// </summary>
        public virtual bool Succeeded => ErrorMessage == null;

        /// <summary>
        /// Returns true if the underlying operation was canceled.
        /// </summary>
        public bool IsCancelled { get; internal set; }

        /// <summary>
        /// Returns true if the error occurred and the error is a critical (i.e. not-recoverable) exception.
        /// </summary>
        public bool IsCriticalFailure => IsCancelled ? !NonCriticalForCancellation(Exception) : IsCritical(Exception);

        /// <summary>
        /// Returns true if the error occurred because of an exception.
        /// </summary>
        public bool HasException => Exception != null;

        /// <summary>
        /// Returns an optional exception instance associated with the result.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Description of the error that occurred.
        /// </summary>
        public readonly string ErrorMessage;

        /// <summary>
        /// Indicates that diagnostics should lazily be computed from exception
        /// </summary>
        private bool _hasLazyDiagnostics;
        private string _diagnostics;

        /// <summary>
        /// Optional verbose diagnostic information about the result (either error or success).
        /// </summary>
        public string Diagnostics
        {
            get
            {
                // Lazily initialize diagnostics (if specified)
                if (_hasLazyDiagnostics && _diagnostics == null && Exception != null)
                {
                    _diagnostics = CreateDiagnostics(Exception);
                }

                return _diagnostics;
            }
            set
            {
                _hasLazyDiagnostics = false;
                _diagnostics = value;
            }
        }

        /// <summary>
        /// Gets or sets elapsed time of corresponding call.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Gets elapsed time, in milliseconds, of corresponding call.
        /// </summary>
        public long DurationMs => (long)Duration.TotalMilliseconds;

        /// <summary>
        /// Check if equal to another.
        /// </summary>
        protected bool EqualsBase(ResultBase other)
        {
            return !(other is null) && ErrorMessage == other.ErrorMessage;
        }

        /// <summary>
        /// Returns true if 'this' and 'other' are not succeeded and both have the same error text.
        /// </summary>
        protected bool ErrorEquals(ResultBase other)
        {
            if (other is null)
            {
                return false;
            }

            if (Succeeded != other.Succeeded)
            {
                return false;
            }

            return ErrorMessage == other.ErrorMessage && Diagnostics == other.Diagnostics;
        }

        /// <nodoc />
        protected virtual string GetSuccessString()
        {
            return "Success";
        }

        /// <inheritdoc />
        public override string ToString() => Succeeded ? GetSuccessString() : GetErrorString();

        /// <summary>
        /// Create a string describing the error (and diagnostics, if applicable)
        /// </summary>
        protected string GetErrorString()
        {
            if (IsCancelled)
            {
                return $"Error=[Canceled] Message=[{ErrorMessage}]";
            }

            if (string.IsNullOrEmpty(Diagnostics))
            {
                return $"Error=[{ErrorMessage}]";
            }

            return $"Error=[{ErrorMessage}] Diagnostics=[{Diagnostics}]";
        }

        /// <summary>
        /// Returns true if a given exception should not be considered critical during cancellation.
        /// </summary>
        public static bool NonCriticalForCancellation(Exception exception)
        {
            return !IsCriticalForCancellation(exception);
        }

        /// <summary>
        /// Returns true if a given exception is critical critical during cancellation.
        /// </summary>
        public static bool IsCriticalForCancellation(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            // InvalidOperationException is not critical during cancellation (because it is possible that the state is not quite right because of natural race conditions).
            return IsCritical(exception) && !(exception is InvalidOperationException);
        }

        /// <summary>
        /// Returns true if the exception is a critical, non-recoverable error.
        /// </summary>
        public static bool IsCritical(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            // Intentionally ignoring unrecoverable CLR exceptions like
            // StackOverflowException, ThreadAbortException and ExecutionEngineException.
            // Because it is unlikely that the user code can handle them anyway.
            // OutOfMemoryException is considered critical, because there two types of them:
            // one is recoverable (when a user tries to allocate a very large array) and some of them
            // are not. We try our best for OOM in this case.
            return exception is NullReferenceException ||
                   exception is TypeLoadException ||
                   exception is ArgumentException ||
                   exception is IndexOutOfRangeException ||
                   exception is UnauthorizedAccessException ||
                   exception is OutOfMemoryException ||
                   exception.GetType().Name == "ContractException" ||
                   exception is InvalidOperationException ||
                   (exception is AggregateException ae && ae.Flatten().InnerExceptions.Any(e => IsCritical(e)));
        }

        /// <summary>
        /// Gets full log event message including inner exceptions from the given exception
        /// </summary>
        private static string GetErrorMessageFromException(Exception exception)
        {
            return string.Join(": " + Environment.NewLine, getExceptionMessages());

            IEnumerable<string> getExceptionMessages()
            {
                for (Exception currentException = exception; currentException != null; currentException = currentException.InnerException)
                {
                    yield return currentException.Message;
                }
            }
        }

        /// <summary>
        /// Gets a preprocessed equivalent of <see cref="Exception.ToString"/> if <see cref="ResultBase.ResultExceptionTextProcessor"/> is specified. Otherwise,
        /// just returns <see cref="Exception.ToString"/>.
        /// </summary>
        internal static string GetExceptionString(Exception ex)
        {
            var processErrorText = ResultExceptionTextProcessor;
            if (processErrorText != null)
            {
                return processErrorText(ex);
            }

            return ex.ToString();
        }

        private static string CreateDiagnostics(Exception exception)
        {
            // Consider using Demystifier here.
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
    }
}
