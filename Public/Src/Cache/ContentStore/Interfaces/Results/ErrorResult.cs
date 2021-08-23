// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq.Expressions;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of a failed operation.
    /// </summary>
    public class ErrorResult : ResultBase
    {
        /// <nodoc />
        public ErrorResult(string errorMessage, string? diagnostics = null)
            : base(Error.FromErrorMessage(errorMessage, diagnostics))
        {
            Contract.RequiresNotNullOrEmpty(errorMessage);
        }

        /// <nodoc />
        public ErrorResult(Exception exception, string? message = null)
            : base(Error.FromException(exception, message))
        {
        }

        /// <nodoc />
        public ErrorResult(ResultBase other, string? message = null)
            : base(other, message)
        {
        }

        /// <summary>
        /// Converts the error result to the given result type.
        /// </summary>
        public TResult AsResult<TResult>() where TResult : ResultBase
        {
            if (ErrorResultConverter<TResult>.TryGetConverter(out var converter, out string? error))
            {
                return converter(this);
            }

            throw new InvalidOperationException(error);
        }

        private static class ErrorResultConverter<TResult>
            where TResult : ResultBase
        {
            private static readonly Func<ErrorResult, TResult>? Convert;
            private static readonly string? ErrorMessage;

            static ErrorResultConverter()
            {
                // It is important to call this method from a static constructor
                // and have another method that will just get the data from the fields
                // to avoid expression generation every time.
                TryGenerateConverter(out Convert, out ErrorMessage);
            }

            public static bool TryGetConverter([NotNullWhen(true)]out Func<ErrorResult, TResult>? result, [NotNullWhen(false)]out string? errorMessage)
            {
                result = Convert;
                errorMessage = ErrorMessage;

#pragma warning disable CS8762 // Parameter must have a non-null value when exiting in some condition.
                return result != null;
#pragma warning restore CS8762 // Parameter must have a non-null value when exiting in some condition.
            }

            private static bool TryGenerateConverter([NotNullWhen(true)]out Func<ErrorResult, TResult>? result, [NotNullWhen(false)]out string? errorMessage)
            {
                var errorResultParameter = Expression.Parameter(typeof(ErrorResult));
                var type = typeof(TResult).Name;

                // Generating the following constructor invocation:
                // new TResult(other: errorResult)
                var constructorInfo = typeof(TResult).GetConstructor(new[] { typeof(ResultBase), typeof(string) });
                if (constructorInfo == null)
                {
                    result = null;
                    errorMessage = $"Constructor '{type}(ResultBase, string)' are not defined for type '{type}'.";
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
