using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.Library.Client;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.Library.Rules.Kusto
{
    public class MockKustoClient : IKustoClient
    {
        private readonly Queue<IReadOnlyList<object>> _results = new Queue<IReadOnlyList<object>>();

        public void Add<T>(IEnumerable<T> results)
        {
            _results.Enqueue(results.Cast<object>().ToList());
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string query, string database, ClientRequestProperties? requestProperties = null)
        {
            var queryResult = _results.Peek();
            _results.Dequeue();

            var castedResult = (IReadOnlyList<T>)queryResult.Select(x =>
            {
                var converted = Convert.ChangeType(x, typeof(T));
                Contract.AssertNotNull(converted);
                return (T)converted;
            }).ToList();

            return Task.FromResult(castedResult);
        }

        public void Dispose()
        {
        }
    }
}
