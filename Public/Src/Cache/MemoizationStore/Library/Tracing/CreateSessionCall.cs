// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    /// <summary>
    ///     Instance of a CreateSession operation for tracing purposes.
    /// </summary>
    public sealed class CreateSessionCall : TracedCall<CacheTracer, CreateSessionResult<ICacheSession>>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static CreateSessionResult<ICacheSession> Run(
            CacheTracer tracer, Context context, string name, Func<CreateSessionResult<ICacheSession>> func)
        {
            using (var call = new CreateSessionCall(tracer, context, name))
            {
                return call.Run(func);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CreateSessionCall"/> class.
        /// </summary>
        private CreateSessionCall(CacheTracer tracer, Context context, string name)
            : base(tracer, context)
        {
            Tracer.CreateSessionStart(context, name);
        }

        /// <inheritdoc />
        protected override CreateSessionResult<ICacheSession> CreateErrorResult(Exception exception)
        {
            return new CreateSessionResult<ICacheSession>(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.CreateSessionStop(Context, Result);
        }
    }
}
