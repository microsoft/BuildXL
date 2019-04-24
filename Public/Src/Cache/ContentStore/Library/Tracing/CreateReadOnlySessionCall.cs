// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <summary>
    ///     Instance of a CreateReadOnlySession operation for tracing purposes.
    /// </summary>
    public sealed class CreateReadOnlySessionCall
        : TracedCall<ContentStoreTracer, CreateSessionResult<IReadOnlyContentSession>>, IDisposable
    {
        /// <summary>
        ///     Run the call.
        /// </summary>
        public static CreateSessionResult<IReadOnlyContentSession> Run(
            ContentStoreTracer tracer, OperationContext context, string name, Func<CreateSessionResult<IReadOnlyContentSession>> func)
        {
            using (var call = new CreateReadOnlySessionCall(tracer, context, name))
            {
                return call.Run(func);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CreateReadOnlySessionCall"/> class.
        /// </summary>
        private CreateReadOnlySessionCall(ContentStoreTracer tracer, OperationContext context, string name)
            : base(tracer, context)
        {
            Tracer.CreateReadOnlySessionStart(Context, name);
        }

        /// <inheritdoc />
        protected override CreateSessionResult<IReadOnlyContentSession> CreateErrorResult(Exception exception)
        {
            return new CreateSessionResult<IReadOnlyContentSession>(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.CreateReadOnlySessionStop(Context, Result);
        }
    }
}
