using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.Library.Client
{
    public interface IKustoClient : IDisposable
    {
        Task<IReadOnlyList<T>> QueryAsync<T>(string query, string database, ClientRequestProperties? requestProperties = null);
    }
}
