// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;

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
    }
}
