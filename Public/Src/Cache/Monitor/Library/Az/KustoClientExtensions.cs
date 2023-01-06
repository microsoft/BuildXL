// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using Kusto.Data.Common;
using Kusto.Cloud.Platform.Data;

namespace BuildXL.Cache.Monitor.App.Az
{
    internal static class KustoClientExtensions
    {
        public static async Task<IEnumerable<T>> QuerySingleResultSetAsync<T>(this ICslQueryProvider cslQueryProvider, string query, string database, ClientRequestProperties? requestProperties = null)
        {
            
            Contract.Requires(string.IsNullOrEmpty(query));
            Contract.Requires(string.IsNullOrEmpty(database));

            requestProperties ??= new ClientRequestProperties();

            return (await cslQueryProvider.ExecuteQueryAsync(database, query, requestProperties)).ToEnumerable<T>();
        }
    }
}
