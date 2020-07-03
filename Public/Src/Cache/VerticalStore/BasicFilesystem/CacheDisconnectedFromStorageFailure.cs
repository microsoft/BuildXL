// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.BasicFilesystem
{
    internal sealed class CacheDisconnectedFromStorageFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly Exception m_exception;

        internal CacheDisconnectedFromStorageFailure(string cacheId, Exception exception)
        {
            m_cacheId = cacheId;
            m_exception = exception;
        }

        public override string Describe()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Cache {0} has disconnected from its underlying storage due to exception {1}",
                m_cacheId,
                m_exception);
        }
    }
}
