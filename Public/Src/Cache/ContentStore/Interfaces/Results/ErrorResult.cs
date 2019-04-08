// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq.Expressions;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of a failed operation.
    /// </summary>
    public class ErrorResult : ResultBase, IEquatable<ErrorResult>
    {
        /// <summary>
        /// Original optional message provided via constructor.
        /// </summary>
        public string Message { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ErrorResult"/> class.
        /// </summary>
        public ErrorResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Contract.Requires(!string.IsNullOrEmpty(errorMessage));
            Message = errorMessage;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ErrorResult" /> class.
        /// </summary>
        public ErrorResult(Exception exception, string message = null)
            : base(exception, message)
        {
            Message = message;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ErrorResult" /> class.
        /// </summary>
        public ErrorResult(ResultBase other, string message = null)
            : base(other, message)
        {
            Message = message;
        }

        /// <inheritdoc />
        public override bool Succeeded => false;

        /// <inheritdoc />
        public bool Equals(ErrorResult other)
        {
            return EqualsBase(other) && other != null;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ErrorResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ErrorMessage?.GetHashCode() ?? 0;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return GetErrorString();
        }

        /// <summary>
        ///     Overloads &amp; operator to behave as AND operator.
        /// </summary>
        public static ErrorResult operator &(ErrorResult result1, ErrorResult result2)
        {
            return new ErrorResult(
                    Merge(result1.ErrorMessage, result2.ErrorMessage, ", "),
                    Merge(result1.Diagnostics, result2.Diagnostics, ", "));
        }

        /// <summary>
        ///     Overloads | operator to behave as OR operator.
        /// </summary>
        public static ErrorResult operator |(ErrorResult result1, ErrorResult result2)
        {
            return new ErrorResult(
                Merge(result1.ErrorMessage, result2.ErrorMessage, ", "),
                Merge(result1.Diagnostics, result2.Diagnostics, ", "));
        }

        /// <summary>
        ///     Merges two strings.
        /// </summary>
        private static string Merge(string s1, string s2, string separator = null)
        {
            if (s1 == null)
            {
                return s2;
            }

            if (s2 == null)
            {
                return s1;
            }

            separator = separator ?? string.Empty;

            return $"{s1}{separator}{s2}";
        }

        /// <summary>
        /// Converts the error result to the given result type.
        /// </summary>
        public TResult AsResult<TResult>() where TResult : ResultBase
        {
            if (ErrorResultConverter<TResult>.TryGetConverter(out var converter, out string error))
            {
                return converter(this);
            }

            throw new InvalidOperationException(error);
        }

        private static class ErrorResultConverter<TResult>
            where TResult : ResultBase
        {
            private static readonly Func<ErrorResult, TResult> Convert;
            private static readonly string ErrorMessage;

            static ErrorResultConverter()
            {
                // It is important to call this method from a static constructor
                // and have another method that will just get the data from the fields
                // to avoid expression generation every time.
                TryGenerateConverter(out Convert, out ErrorMessage);
            }

            public static bool TryGetConverter(out Func<ErrorResult, TResult> result, out string errorMessage)
            {
                result = Convert;
                errorMessage = ErrorMessage;
                return result != null;
            }

            private static bool TryGenerateConverter(out Func<ErrorResult, TResult> result, out string errorMessage)
            {
                var errorResultParameter = Expression.Parameter(typeof(ErrorResult));
                var type = typeof(TResult).Name;

                // Generating the following constructor invocation:
                // new TResult(other: errorResult)
                var constructorInfo = typeof(TResult).GetConstructor(new[] { typeof(ResultBase), typeof(string) });
                if (constructorInfo == null)
                {
                    result = null;
                    errorMessage = $"Constructor '{type}(ResultBase, string)' is not defined for type '{type}'.";
                    return false;
                }

                try
                {
                    var convertExpression = Expression.Lambda<Func<ErrorResult, TResult>>(
                        body: Expression.New(
                            constructor: constructorInfo,
                            arguments: new Expression[]
                                       {
                                            errorResultParameter,
                                            Expression.Constant(null, typeof(string)),
                                       }),
                        parameters: new[] { errorResultParameter });
                    result = convertExpression.Compile();
                    errorMessage = null;
                    return true;
                }
                catch (Exception e)
                {
                    result = null;
                    errorMessage = $"Failed creating type converter to '{type}'. Exception: {e}";
                    return false;
                }
            }
        }
    }
}
