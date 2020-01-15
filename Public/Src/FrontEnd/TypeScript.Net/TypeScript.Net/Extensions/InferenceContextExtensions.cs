// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    internal static class InferenceContextExtensions
    {
        /// <nodoc />
        public static ITypeMapper GetOrSetMapper<T>(this IInferenceContext @this, T data, Func<IInferenceContext, T, ITypeMapper> factory)
        {
            return @this.Mapper ?? (@this.Mapper = factory(@this, data));
        }
    }
}
