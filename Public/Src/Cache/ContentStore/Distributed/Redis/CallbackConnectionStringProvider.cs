// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Implementation of <see cref="IConnectionStringProvider"/> that gives back a connection string specified via a callback.
    /// </summary>
    public class CallbackConnectionStringProvider : IConnectionStringProvider
    {
        private readonly Func<Task<string>> _callback;

        public CallbackConnectionStringProvider(Func<Task<string>> callback)
        {
            _callback = callback;
        }

        public async Task<ConnectionStringResult> GetConnectionString() => ConnectionStringResult.CreateSuccess(await _callback());
    }
}
