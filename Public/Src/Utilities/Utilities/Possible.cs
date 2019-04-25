// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents the result of some possibly-successful function.
    /// One should read <see cref="Possible{TResult}" /> as 'possibly TResult, but Failure otherwise'.
    /// </summary>
    /// <remarks>
    /// This struct is shorthand for <see cref="Possible{TResult, TFailure}"/> when TFailure is always a <see cref="Failure"/>.
    /// In general, it is very helpful to use special error type for every function, but in practice, almost all the time
    /// more generic possible is using <see cref="Failure"/> as a TFailure generic argument.
    /// This version is more lightweight and gives more chances to generic type inference. C# compiler can easily infer one generic
    /// argument if there is a method that takes a value of generic type, but there is no way for it to infer just one generic type.
    /// </remarks>
    // TODO: this struct has a tons of code duplication with Possible<TResult, TFailure>
    // Unfortunately, we can't use inheritance without switching to classes and paying reasonable cost at runtime.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct Possible<TResult>
    {
        // Note that TFailure is constrained to a ref type, so we can use null as a success-or-fail marker.
        private readonly Failure m_failure;
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
        public Possible(Failure failure)
        {
            Contract.Requires(failure != null);
            m_failure = failure;
            m_result = default(TResult);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator Possible<TResult>(TResult result)
        {
            return new Possible<TResult>(result);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator Possible<TResult, Failure>(Possible<TResult> result)
        {
            return result.Succeeded ? new Possible<TResult, Failure>(result.Result) : new Possible<TResult, Failure>(result.Failure);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator Possible<TResult>(Possible<TResult, Failure> possible)
        {
            return possible.Succeeded ? new Possible<TResult>(possible.Result) : new Possible<TResult>(possible.Failure);
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator Possible<TResult>(Failure failure)
        {
            Contract.Requires(failure != null);
            return new Possible<TResult>(failure);
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
                if (!Succeeded)
                {
                    // Calling a method to help inlining this property accessor for a successful path.
                    RaiseResultPreconditionViolation();
                }

                return m_result;
            }
        }

        private void RaiseResultPreconditionViolation()
        {
            Contract.Requires(false,
                "The Possible struct must have succeeded to access the result." +
                Environment.NewLine +
                "Possible struct failure: " + m_failure?.DescribeIncludingInnerFailures());
        }

        /// <summary>
        /// Failure, available only if not <see cref="Succeeded" />.
        /// </summary>
        public Failure Failure
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
        public Possible<TResult, Failure> WithGenericFailure()
        {
            return this;
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
        public Possible<TResult2> Then<TResult2>(Func<TResult, Possible<TResult2>> binder)
        {
            Contract.Requires(binder != null);
            return Succeeded ? binder(m_result) : new Possible<TResult2>(m_failure);
        }

        /// <summary>
        /// Async version of <c>Then{TResult2}</c>
        /// </summary>
        [Pure]
        public Task<Possible<TResult2>> ThenAsync<TResult2>(Func<TResult, Task<Possible<TResult2>>> binder)
        {
            Contract.Requires(binder != null);
            return Succeeded ? binder(m_result) : Task.FromResult(new Possible<TResult2>(m_failure));
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
            Func<Failure, Possible<TResult2, TFailure2>> failureBinder)
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
        public Possible<TResult2> Then<TResult2>(Func<TResult, TResult2> thenFunc)
        {
            Contract.Requires(thenFunc != null);
            return Succeeded ? new Possible<TResult2>(thenFunc(m_result)) : new Possible<TResult2>(m_failure);
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
        public Possible<TResult2> Then<TData, TResult2>(TData data, Func<TData, TResult, TResult2> thenFunc)
        {
            Contract.Requires(thenFunc != null);
            return Succeeded ? new Possible<TResult2>(thenFunc(data, m_result)) : new Possible<TResult2>(m_failure);
        }
    }

    /// <summary>
    /// Helper class with factory methods that allows generic type inference for <see cref="Possible{TResult}"/> instances.
    /// </summary>
    public static class Possible
    {
        /// <nodoc />
        public static Possible<TResult> Create<TResult>(TResult result)
        {
            return result;
        }
    }
}
