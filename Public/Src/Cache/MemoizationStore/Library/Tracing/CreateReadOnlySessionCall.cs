// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    /// <summary>
    ///     Instance of a CreateReadOnlySession operation for tracing purposes.
    /// </summary>
    public sealed class CreateReadOnlySessionCall
        : TracedCall<CacheTracer, CreateSessionResult<IReadOnlyCacheSession>>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static CreateSessionResult<IReadOnlyCacheSession> Run(
            CacheTracer tracer, Context context, string name, Func<CreateSessionResult<IReadOnlyCacheSession>> func)
        {
            using (var call = new CreateReadOnlySessionCall(tracer, context, name))
            {
                return call.Run(func);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CreateReadOnlySessionCall"/> class.
        /// </summary>
        private CreateReadOnlySessionCall(CacheTracer tracer, Context context, string name)
            : base(tracer, context)
        {
            Tracer.CreateReadOnlySessionStart(context, name);
        }

        /// <inheritdoc />
        protected override CreateSessionResult<IReadOnlyCacheSession> CreateErrorResult(Exception exception)
        {
            return new CreateSessionResult<IReadOnlyCacheSession>(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.CreateReadOnlySessionStop(Context, Result);
        }
    }
}
