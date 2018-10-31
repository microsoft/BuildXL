// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents the result of some possibly-successful function.
    /// One should read <see cref="Possible{TResult,TFailure}" /> as 'possibly TResult, but TFailure otherwise'.
    /// </summary>
    /// <remarks>
    /// This is similar to the 'Either' monad, where the binding <c>Then</c> operates on Left (result) or forwards Right (failure)
    /// and Haskell's 'Exceptional' monad (which is itself describe as similar to 'Either', but with more convention).
    /// The type of <typeparamref name="TFailure" /> may encode rich error information (why / how did the operation fail?) via
    /// <see cref="BuildXL.Utilities.Failure{TContent}" />, or a captured recoverable exception via <see cref="RecoverableExceptionFailure" />.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct Possible<TResult, TFailure>
        where TFailure : Failure
    {
        // Note that TFailure is constrained to a ref type, so we can use null as a success-or-fail marker.
        private readonly TFailure m_failure;
        private readonly TResult m_result;

        /// <summary>
        /// Creates a success outcome.
        /// </summary>
        public Possible(TResult result)
        {
            m_failure = null;
            m_result = result;
        }

        /// <summary>
        /// Creates a failure outcome.
        /// </summary>
        public Possible(TFailure failure)
        {
            Contract.Requires(failure != null);
            m_failure = failure;
            m_result = default(TResult);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator Possible<TResult, TFailure>(TResult result)
        {
            return new Possible<TResult, TFailure>(result);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator Possible<TResult, TFailure>(TFailure failure)
        {
            Contract.Requires(failure != null);
            return new Possible<TResult, TFailure>(failure);
        }

        /// <summary>
        /// Indicates if this is a successful outcome (<see cref="Result" /> available) or not (<see cref="Failure" /> available).
        /// </summary>
        public bool Succeeded => m_failure == null;

        /// <summary>
        /// Result, available only if <see cref="Succeeded" />.
        /// </summary>
        public TResult Result
        {
            get
            {
                Contract.Requires(Succeeded);
                return m_result;
            }
        }

        /// <summary>
        /// Failure, available only if not <see cref="Succeeded" />.
        /// </summary>
        public TFailure Failure
        {
            get
            {
                Contract.Requires(!Succeeded);
                return m_failure;
            }
        }

        /// <summary>
        /// Erases the specific failure type of this instance.
        /// </summary>
        [Pure]
        public Possible<TResult> WithGenericFailure()
        {
            return Succeeded ? new Possible<TResult>(m_result) : new Possible<TResult>(m_failure);
        }

        /// <summary>
        /// Monadic bind. Returns a new result or failure if this one <see cref="Succeeded" />; otherwise
        /// forwards the current <see cref="Failure" />.
        /// </summary>
        /// <example>
        ///     <![CDATA[
        /// Possible<string> maybeString = TryGetString();
        /// Possible<int> maybeInt = maybeString.Then(s => TryParse(s));
        /// ]]>
        /// </example>
        [Pure]
        public Possible<TResult2, TFailure> Then<TResult2>(Func<TResult, Possible<TResult2, TFailure>> binder)
        {
            Contract.Requires(binder != null);
            return Succeeded ? binder(m_result) : new Possible<TResult2, TFailure>(m_failure);
        }

        /// <summary>
        /// Async version of <c>Then{TResult2}</c>
        /// </summary>
        [Pure]
        public Task<Possible<TResult2, TFailure>> ThenAsync<TResult2>(Func<TResult, Task<Possible<TResult2, TFailure>>> binder)
        {
            Contract.Requires(binder != null);
            return Succeeded ? binder(m_result) : Task.FromResult(new Possible<TResult2, TFailure>(m_failure));
        }

        /// <summary>
        /// Dual version of <c>Then{TResult2}</c> which calls <paramref name="resultBinder"/> on success
        /// or <paramref name="failureBinder"/> on failure. Unlike single bind, since the failure type is not constrained to
        /// that of the input value, a new type can be specified as <typeparamref name="TFailure2"/>.
        /// </summary>
        /// <example>
        ///     <![CDATA[
        /// Possible<string, AlphaFailure> maybeString = TryGetString();
        /// Possible<int, BetaFailure> maybeInt = maybeString.Then<int, BetaFailure>(s => TryParse(s), f => new BetaFailure(...));
        /// ]]>
        /// </example>
        [Pure]
        public Possible<TResult2, TFailure2> Then<TResult2, TFailure2>(
            Func<TResult, Possible<TResult2, TFailure2>> resultBinder,
            Func<TFailure, Possible<TResult2, TFailure2>> failureBinder)
            where TFailure2 : Failure
        {
            Contract.Requires(resultBinder != null);
            Contract.Requires(failureBinder != null);
            return Succeeded ? resultBinder(m_result) : failureBinder(m_failure);
        }

        /// <summary>
        /// Monadic 'then' (Haskell operator &gt;&gt;. Returns a new, transformed <see cref="Result" /> if this one <see cref="Succeeded" />; otherwise
        /// forwards the current <see cref="Failure" />.
        /// </summary>
        /// <example>
        ///     <![CDATA[
        /// Possible<string> maybeString = TryGetString();
        /// Possible<int> maybeInt = maybeString.Then(s => s.Length);
        /// ]]>
        /// </example>
        [Pure]
        public Possible<TResult2, TFailure> Then<TResult2>(Func<TResult, TResult2> thenFunc)
        {
            Contract.Requires(thenFunc != null);
            return Succeeded ? new Possible<TResult2, TFailure>(thenFunc(m_result)) : new Possible<TResult2, TFailure>(m_failure);
        }
    }

    /// <summary>
    /// Base failure type. A failure is like an exception, but less exceptional.
    /// </summary>
    /// <remarks>
    /// All failure types for a <see cref="Possible{TResult,TFailure}"/> are constrained to be subclasses of this
    /// (a reference type) rather than some equivalent interface. This allows safely using the failure field of a possible
    /// result such that <c>null</c> indicates success (a value-type implementing some <c>IFailure</c> would silently break that check).
    /// TODO: Failure / Possible are good candidates for instrumentation (we could generically log them all as they are created to ETW).
    /// </remarks>
    public abstract class Failure
    {
        /// <nodoc />
        protected Failure(Failure innerFailure = null)
        {
            InnerFailure = innerFailure;
        }

        /// <summary>
        /// Returns a lower-level failure (if present) that caused this one.
        /// </summary>
        /// <remarks>
        /// A top-level may have a linear chain of inner failures. <see cref="DescribeIncludingInnerFailures" />
        /// can flatten a chain of inner failures to a user-readable string.
        /// </remarks>
        public Failure InnerFailure { get; }

        /// <summary>
        /// Describes this failure for the purpose of user-facing logging.
        /// The chain of inner failures is included in the message.
        /// </summary>
        public string DescribeIncludingInnerFailures()
        {
            using (PooledObjectWrapper<StringBuilder> wrapper = Pools.StringBuilderPool.GetInstance())
            {
                var instance = wrapper.Instance;
                bool first = true;
                for (Failure f = this; f != null; f = f.InnerFailure)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        instance.AppendLine(": ");
                    }

                    instance.Append(f.Describe());
                }

                return instance.ToString();
            }
        }

        /// <summary>
        /// Describes this failure for the purpose of user-facing logging.
        /// </summary>
        public abstract string Describe();

        /// <summary>
        /// Returns a throwable exception representation of this failure.
        /// </summary>
        /// <remarks>
        /// Explicit conversion to an exception is sometimes convenient for using <see cref="Possible{TResult,TFailure}"/> style
        /// functions where exceptions are the norm.
        /// </remarks>
        public abstract BuildXLException CreateException();

        /// <summary>
        /// Throws this failure as an exception.
        /// </summary>
        /// <remarks>
        /// Conversion to and throwing as an exception is sometimes convenient for using <see cref="Possible{TResult,TFailure}"/> style
        /// functions where exceptions are the norm.
        /// </remarks>
        public abstract BuildXLException Throw();

        /// <summary>
        /// Creates a failure wrapping this one, with additional content to annotate the failure.
        /// </summary>
        /// <example>
        /// <![CDATA[
        /// return someFailure.Annotate("Couldn't open the file to re-arrange all the bits");
        /// ]]>
        /// </example>
        public Failure<TContent> Annotate<TContent>(TContent content)
        {
            return new Failure<TContent>(content, this);
        }
    }

    /// <summary>
    /// Represents a failure with associated content (something describing the why or how of the failure).
    /// </summary>
    public sealed class Failure<TContent> : Failure
    {
        /// <nodoc />
        public Failure(TContent content, Failure innerFailure = null)
            : base(innerFailure)
        {
            Content = content;
        }

        /// <nodoc />
        public TContent Content { get; }

        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return new BuildXLException("Failure: " + DescribeIncludingInnerFailures());
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return Content == null
                ? "<null failure content of type " + typeof(TContent).Name + ">"
                : Content.ToString();
        }
    }

    /// <summary>
    /// Represents a failure from catching an exception. The original exception throw is captured, if possible,
    /// so that later re-throwing with <see cref="Throw" /> preserves the original stack (transient capture as a
    /// <see cref="Possible{TResult,TFailure}" /> becomes transparent).
    /// </summary>
    /// <remarks>
    /// This failure type wouldn't need to exist in the absence of existing code throwing <see cref="BuildXLException" />
    /// (instead, we could just construct and stacks of failures instead of ever throwing). This type allows injecting
    /// <see cref="Possible{TResult,TFailure}" /> style control flow 'in the middle' without disrupting callers (they can
    /// call <see cref="Throw" /> on failure) or callees (we can capture all information, including original stack, that they
    /// throw. For example:
    /// <![CDATA[
    /// int OriginalParse(string s) {
    ///     if (...) { throw new BuildXLException("Bad string");
    ///     ...
    /// }
    ///
    /// Possible<int, IFailure> TryParse(string s) {
    ///     try {
    ///         return OriginalParse(s);
    ///     } catch (BuildXLException ex) {
    ///         // We capture the stack including OriginalParse, as a falure.
    ///         return new RecoverableExceptionFailure(ex);
    ///     }
    /// }
    ///
    /// void DoStuff(string s) {
    ///     Possible<int, IFailure> maybeParsed = TryParse(s);
    ///     // The caller happens to decide to rethrow it (stack preserved);
    ///     // On the plus side, the caller can immediately start using TryParse instead of OriginalParse.
    ///     if (!maybeParsed.Succeeded) { throw maybeParsed.Throw(); }
    /// }
    /// ]]>
    /// </remarks>
    public sealed class RecoverableExceptionFailure : Failure
    {
        private readonly ExceptionDispatchInfo m_exceptionDispatchInfo;

        /// <nodoc />
        public RecoverableExceptionFailure(BuildXLException exception)
        {
            Contract.Requires(exception != null);
            m_exceptionDispatchInfo = ExceptionDispatchInfo.Capture(exception);
        }

        /// <nodoc />
        public BuildXLException Exception => (BuildXLException)m_exceptionDispatchInfo.SourceException;

        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return Exception;
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            m_exceptionDispatchInfo.Throw();
            throw new InvalidOperationException("Unreachable");
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return Exception.LogEventMessage;
        }
    }
}
