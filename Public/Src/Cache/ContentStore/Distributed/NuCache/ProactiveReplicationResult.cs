// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Result of proactive replication for customized result logging 
    /// </summary>
    public class ProactiveReplicationResult : Result<(int succeeded, int failed)>
    {
        /// <nodoc />
        public ProactiveReplicationResult(int succeeded, int failed) : base((succeeded, failed))
        {
        }

        /// <nodoc />
        public ProactiveReplicationResult(ResultBase other, string message = null) : base(other, message)
        {
        }

        /// <inheritdoc />
        protected override string GetSuccessString() => $"{base.GetSuccessString()} (Succeeded={Value.succeeded}, Failed={Value.failed}, Total={Value.succeeded + Value.failed})";
    }
}
