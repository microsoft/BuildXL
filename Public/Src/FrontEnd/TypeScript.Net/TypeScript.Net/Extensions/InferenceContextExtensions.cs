// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
