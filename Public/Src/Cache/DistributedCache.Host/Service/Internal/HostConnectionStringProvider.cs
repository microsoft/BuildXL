// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Host.Service.Internal
{
    internal class HostConnectionStringProvider : IConnectionStringProvider
    {
        private readonly ILogger _logger;

        private readonly IDistributedCacheServiceHost _host;
        private readonly string _secretKeyName;

        public HostConnectionStringProvider(IDistributedCacheServiceHost host, string secretKeyName, ILogger logger)
        {
            _host = host;
            _secretKeyName = secretKeyName;
            _logger = logger;
        }

        public Task<ConnectionStringResult> GetConnectionString()
        {
            return Task.Run(() =>
                            {
                                try
                                {
                                    _logger.Debug("Deserializing secret for CB with secret name {0}.", _secretKeyName);
                                    var secret = _host.GetSecretStoreValue(_secretKeyName);
                                    return ConnectionStringResult.CreateSuccess(secret);
                                }
                                catch (Exception ex)
                                {
                                    return ConnectionStringResult.CreateFailure(ex);
                                }
                            });
        }
    }
}
