// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.ExceptionServices;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// A base class that represents the result of an operation.
    /// </summary>
    /// <remarks>
    /// The result can have one of 3 states:
    /// * Success + optional success diagnostics (when <see cref="Error"/> property is null and <see cref="IsCancelled"/> property is false).
    /// * Failure (when the <see cref="Error"/> property is not null)
    /// * Cancelled (when <see cref="IsCancelled"/> flag is true)
    /// </remarks>
    public abstract class ResultBase : IEquatable<ResultBase>
    {
        /// <summary>
        /// Constructor for creating successful result instances.
        /// </summary>
        protected ResultBase()
        {   
        }

        /// <summary>
        /// A legacy constructor that constructs a new instance of a failed result.
        /// </summary>
        protected ResultBase(string errorMessage, string? diagnostics)
            : this(Error.FromErrorMessage(errorMessage, diagnostics))
        {
        }

        /// <summary>
        /// Creates a new instance of a failed result if <paramref name="error"/>.
        /// </summary>
        protected ResultBase(Error error)
        {
            Error = error;
        }

        /// <summary>
        /// Creates a new instance of a result that failed with a given exception.
        /// </summary>
        protected ResultBase(Exception exception, string? message = null)
        {
            Contract.Requires(exception != null);

            if (exception is ResultPropagationException other)
            {
                Error = other.Result.Error;
                IsCancelled = other.Result.IsCancelled;
            }
            else
            {
                Error = Error.FromException(exception, message);
            }
        }

        /// <summary>
        /// Creates a new instance of the result from another result instance.
        /// </summary>
        protected ResultBase(ResultBase other, string? message)
        {
            Contract.Requires(other != null);
            Contract.Requires(!other.Succeeded, "Can't create a result from another successful result");

            Error = other.Error?.WithExtraMessage(message);
            IsCancelled = other.IsCancelled;
        }

        /// <summary>
        /// True if the error is critical (only possible when the error has an exception).
        /// </summary>
        private bool _isCritical = false;
        private TimeSpan _duration;
        private string? _successDiagnostics;

        /// <summary>
        /// Optional verbose diagnostic information about the successful result or a diagnostics information about the error.
        /// </summary>
        public string? Diagnostics
        {
            get
            {
                return Succeeded ? _successDiagnostics : Error?.Diagnostics;
            }
        }

        /// <nodoc />
        public void SetDiagnosticsForSuccess(string diagnostics)
        {
            Contract.Requires(Succeeded, "Can't change a diagnostics message after a non-successful result was constructed.");
            _successDiagnostics = diagnostics;
        }

        /// <summary>
        /// Mark the result as critical for tracing purposes.
        /// </summary>
        public void MakeCritical()
        {
            // Only exceptions can be critical. So if the operation doesn't have it, do nothing.
            if (Exception != null)
            {
                _isCritical = true;
            }
        }

        /// <summary>
        /// Mark the result as cancelled.
        /// </summary>
        internal void MarkCancelled() => IsCancelled = true;

        /// <summary>
        /// Sets the duration of the operation that this result instance represents for.
        /// </summary>
        public void SetDuration(TimeSpan duration) => _duration = duration;

        /// <summary>
        /// Returns an error if the underlying operation had failed.
        /// </summary>
        public virtual Error? Error { get; }

        /// <summary>
        /// Gets an optional exception that happen during the operation.
        /// </summary>
        public Exception? Exception => Error?.Exception;

        /// <summary>
        /// Re-throws the exception.
        /// </summary>
        public void ReThrow()
        {
            Contract.Requires(Exception != null);
            ExceptionDispatchInfo.Capture(Exception).Throw();
        }

        /// <summary>
        /// Gets an error message (non null if the result is unsuccessful).
        /// </summary>
        public string? ErrorMessage => Error?.ErrorMessage;

        /// <summary>
        /// Indicates if this is a successful outcome or not.
        /// </summary>
        /// <remarks>
        /// The property is virtual because a derived class may decide adding special attributes, like MemberNotNullAttribute
        /// to mark that some other properties are not null.
        /// The derived types should not change the semantics of this method.
        /// </remarks>
        public virtual bool Succeeded => Error == null && !IsCancelled;

        /// <summary>
        /// Returns true if the underlying operation was canceled.
        /// </summary>
        public bool IsCancelled { get; internal set; }

        /// <summary>
        /// Returns true if the error occurred and the error is a critical (i.e. not-recoverable) exception.
        /// </summary>
        public bool IsCriticalFailure => _isCritical || Error?.IsCritical(IsCancelled) == true;

        /// <summary>
        /// Gets or sets elapsed time of corresponding call.
        /// </summary>
        public TimeSpan Duration => _duration;

        /// <summary>
        /// Gets elapsed time, in milliseconds, of corresponding call.
        /// </summary>
        public long DurationMs => (long)Duration.TotalMilliseconds;

        /// <summary>
        /// Returns true if two successful instances are equal.
        /// </summary>
        protected virtual bool SuccessEquals(ResultBase other)
        {
            return true;
        }

        /// <summary>
        /// Returns true if 'this' and 'other' are not succeeded and both have the same error text.
        /// </summary>
        protected bool ErrorEquals(ResultBase other)
        {
            Contract.Requires(!Succeeded);
            Contract.Requires(!other.Succeeded);

            if (IsCancelled != other.IsCancelled)
            {
                return false;
            }

            if (Error is null && other.Error is not null)
            {
                return false;
            }

            return Error?.Equals(other.Error) == true;
        }

        /// <inheritdoc />
        public bool Equals(ResultBase? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (GetType() != other.GetType())
            {
                return false;
            }

            if (Succeeded != other.Succeeded ||
                IsCancelled != other.IsCancelled)
            {
                return false;
            }

            if (Succeeded && other.Succeeded)
            {
                return SuccessEquals(other);
            }

            return ErrorEquals(other);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as ResultBase);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (IsCancelled, Succeeded, Error).GetHashCode();
        }

        /// <nodoc />
        protected virtual string GetSuccessString()
        {
            return Diagnostics != null ? $"Success Diagnostics=[{Diagnostics}]" : "Success";
        }

        /// <inheritdoc />
        public override string ToString() => Succeeded ? GetSuccessString() : GetErrorString();

        /// <summary>
        /// Create a string describing the error (and diagnostics, if applicable)
        /// </summary>
        protected virtual string GetErrorString()
        {
            if (IsCancelled)
            {
                var result = "Error=[Canceled]";
                if (Error != null)
                {
                    result += " " + Error;
                }

                return result;
            }

            Contract.Assert(Error != null);
            return Error.ToString();
        }

        /// <summary>
        /// Returns true if a given exception should not be considered critical during cancellation.
        /// </summary>
        public static bool NonCriticalForCancellation(Exception? exception)
        {
            return !IsCriticalForCancellation(exception);
        }

        /// <summary>
        /// Returns true if a given exception is critical critical during cancellation.
        /// </summary>
        public static bool IsCriticalForCancellation(Exception? exception)
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
        public static bool IsCritical(Exception? exception)
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
                   exception is DivideByZeroException ||
                   (exception is AggregateException ae && ae.Flatten().InnerExceptions.Any(e => IsCritical(e)));
        }

        /// <summary>
        ///     Merges two strings.
        /// </summary>
        protected internal static string Merge([NotNullIfNotNull("s1")] string? s1, [NotNullIfNotNull("s2")] string? s2, string separator)
        {
            if (s1 == null)
            {
                return s2!;
            }

            if (s2 == null)
            {
                return s1;
            }

            return $"{s1}{separator}{s2}";
        }

        /// <summary>
        /// Merges two failures into one result instance.
        /// </summary>
        protected static TResult MergeFailures<TResult>(TResult left, TResult right, Func<TResult> defaultResultCtor, Func<Error, TResult> fromError) where TResult : ResultBase
        {
            Contract.Requires(!left.Succeeded && !right.Succeeded);

            // Its hard to tell which exact semantics here is the best when two operations failed
            // but when only one operation was canceled.
            // The current behavior is: the final result is considered cancelled only when
            // all the operations were canceled.
            bool isCanceled = left.IsCancelled && right.IsCancelled;

            // left.Error and right.Error may be null because both results may be canceled.

            var error = Error.Merge(left.Error, right.Error, ", ");

            TResult result;

            // The error maybe null if both operations were cancelled
            if (error == null)
            {
                Contract.Assert(isCanceled);
                result = defaultResultCtor();;
            }
            else
            {
                result = fromError(error);
            }

            result.IsCancelled = isCanceled;
            return result;
        }
    }
}
