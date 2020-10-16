using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Monitor.App.Az;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.Library.Client
{
    public class KustoClient : IKustoClient
    {
        private readonly ICslQueryProvider _client;

        public KustoClient(ICslQueryProvider client)
        {
            _client = client;
        }

        public async Task<IReadOnlyList<T>> QueryAsync<T>(string query, string database, ClientRequestProperties? requestProperties = null)
        {
            return (await _client.QuerySingleResultSetAsync<T>(query, database, requestProperties)).ToList();
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
