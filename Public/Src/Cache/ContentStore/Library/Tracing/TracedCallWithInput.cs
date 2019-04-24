// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Machinery for tracing the execution of some call/code and logs the input.
    /// </summary>
    public abstract class TracedCallWithInput<TTracer, TInput, TResult> : TracedCall<TTracer, TResult>
        where TResult : ResultBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TracedCallWithInput{TTracer,TInput,TResult}"/> class without a counter.
        /// </summary>
        protected TracedCallWithInput(TTracer tracer, OperationContext context, TInput input)
            : base(tracer, context)
        {
            Input = input;
        }

        /// <summary>
        ///     Gets input of the call being traced.
        /// </summary>
        protected TInput Input { get; }
    }
}
