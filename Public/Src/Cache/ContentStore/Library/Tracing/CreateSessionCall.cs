// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a CreateSession operation for tracing purposes.
    /// </summary>
    public sealed class CreateSessionCall
        : TracedCall<ContentStoreTracer, CreateSessionResult<IContentSession>>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static CreateSessionResult<IContentSession> Run(
            ContentStoreTracer tracer, OperationContext context, string name, Func<CreateSessionResult<IContentSession>> func)
        {
            using (var call = new CreateSessionCall(tracer, context, name))
            {
                return call.Run(func);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CreateSessionCall"/> class.
        /// </summary>
        private CreateSessionCall(ContentStoreTracer tracer, OperationContext context, string name)
            : base(tracer, context)
        {
            Tracer.CreateSessionStart(Context, name);
        }

        /// <inheritdoc />
        protected override CreateSessionResult<IContentSession> CreateErrorResult(Exception exception)
        {
            return new CreateSessionResult<IContentSession>(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.CreateSessionStop(Context, Result);
        }
    }
}
