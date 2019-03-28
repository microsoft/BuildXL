// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!
#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    /// Provides a capability to match a given object against a preconfigured type (<see cref="ArgumentType"/>)
    /// and subsequently invoke a preconfigured function handler (<see cref="Handler"/>) on it.
    /// </summary>
    /// <typeparam name="TResult">The return type of the preconfigured handler.</typeparam>
    public abstract class CaseMatcher<TResult>
    {
        public Type ArgumentType { get; }

        public Func<object, TResult> Handler { get; }

        protected CaseMatcher(Type type, Func<object, TResult> handler)
        {
            Contract.Requires(type != null);
            Contract.Requires(handler != null);

            ArgumentType = type;
            Handler = handler;
        }

        public virtual bool Matches(object value)
        {
            return ArgumentType.IsInstanceOfType(value);
        }

        public virtual TResult Invoke(object value)
        {
            return Handler(value);
        }
    }

    public sealed class CaseMatcher : CaseMatcher<object>
    {
        public CaseMatcher(Type type, Action<object> handler)
            : base(type, (obj) =>
            {
                handler(obj);
                return null;
            }) { }
    }

    /// <summary>
    /// Specializes <see cref="CaseMatcher{TResult}"/> by providing type parameter for the
    /// first argument of the preconfigured handler (<see cref="CaseMatcher{TResult}.Handler"/>).
    /// </summary>
    /// <typeparam name="T">Type of the handler's argument.</typeparam>
    /// <typeparam name="TResult">Return type of the handler.</typeparam>
    public sealed class CaseMatcher<T, TResult> : CaseMatcher<TResult>
    {
        public CaseMatcher(Func<T, TResult> handler)
            : base(typeof(T), obj => handler((T)obj)) { }
    }

    /// <summary>
    /// Simple utility class for matching an object against a type.
    /// </summary>
    public static class Matcher
    {
        public static CaseMatcher<T, TResult> Case<T, TResult>(Func<T, TResult> handler)
        {
            return new CaseMatcher<T, TResult>(handler);
        }

        public static CaseMatcher Case<T>(Action<T> handler)
        {
            return new CaseMatcher(typeof(T), (obj) => handler((T)obj));
        }

        public static TResult Match<TResult>(object value, IEnumerable<CaseMatcher<TResult>> cases, TResult defaultResult = default(TResult))
        {
            var matchingCase = cases.FirstOrDefault(c => c.Matches(value));
            return matchingCase != null
                ? matchingCase.Invoke(value)
                : defaultResult;
        }

        public static void Match(object value, IEnumerable<CaseMatcher> cases)
        {
            var matchingCase = cases.FirstOrDefault(c => c.Matches(value));
            if (matchingCase != null)
            {
                matchingCase.Invoke(value);
            }
        }
    }
}
