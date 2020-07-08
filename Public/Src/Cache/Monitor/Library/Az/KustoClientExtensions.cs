// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using Kusto.Cloud.Platform.Data;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Az
{
    internal static class KustoClientExtensions
    {
        public static async Task<ObjectReader<T>> QuerySingleResultSetAsync<T>(this ICslQueryProvider cslQueryProvider, string query, string database, ClientRequestProperties? requestProperties = null)
        {
            Contract.RequiresNotNullOrEmpty(query);
            Contract.RequiresNotNullOrEmpty(database);

            requestProperties ??= new ClientRequestProperties();

            var dataReader = await cslQueryProvider.ExecuteQueryAsync(database, query, requestProperties);
            return new ObjectReader<T>(dataReader, disposeReader: true, nameBasedColumnMapping: true);
        }
    }
}
