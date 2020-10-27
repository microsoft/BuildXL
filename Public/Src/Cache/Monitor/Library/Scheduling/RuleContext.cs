// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.Monitor.App.Scheduling
{
    public class RuleContext
    {
        public Guid RunGuid { get; }

        public DateTime RunTimeUtc { get; }

        public CancellationToken CancellationToken { get; }

        public RuleContext(Guid runGuid, DateTime runTimeUtc, CancellationToken cancellationToken)
        {
            RunGuid = runGuid;
            RunTimeUtc = runTimeUtc;
            CancellationToken = cancellationToken;
        }

        public OperationContext IntoOperationContext(ILogger logger)
        {
            var tracingContext = new Context(logger);
            return IntoOperationContext(tracingContext);
        }

        public OperationContext IntoOperationContext(Context tracingContext)
        {
            return new OperationContext(tracingContext, CancellationToken);
        }
    }
}
