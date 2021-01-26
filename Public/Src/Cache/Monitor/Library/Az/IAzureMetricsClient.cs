// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Monitor.Models;
using System.Threading;

namespace BuildXL.Cache.Monitor.Library.Az
{
    internal interface IAzureMetricsClient
    {
        Task<Dictionary<MetricName, List<MetricValue>>> GetMetricsWithDimensionAsync(
            string resourceUri,
            IReadOnlyList<MetricName> metrics,
            string dimension,
            DateTime startTimeUtc, DateTime endTimeUtc,
            TimeSpan samplingInterval,
            IReadOnlyList<AggregationType> aggregations,
            CancellationToken cancellationToken = default);
    }
}
