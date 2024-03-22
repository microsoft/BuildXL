// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using RocksDbSharp;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    public record ColumnFamilyStats(long? TotalLiveContentSize, long? TotalLiveFileSize, long? TotalFileSize);

    internal static class RocksDbUtilities
    {
        private static readonly Tracer Tracer = new Tracer(nameof(RocksDbUtilities));

        private static readonly string LiveDataSizeBytes = "rocksdb.estimate-live-data-size";
        private static readonly string LiveFileSizeBytes = "rocksdb.live-sst-files-size";
        private static readonly string TotalFileSizeBytes = "rocksdb.total-sst-files-size";

        public static Dictionary<string, ColumnFamilyStats> GetStatisticsByColumnFamily(this RocksDb db, OperationContext context)
        {
            var result = new Dictionary<string, ColumnFamilyStats>();
            foreach (KeyValuePair<string, ColumnFamilyHandle> kvp in db.GetColumnFamilyNames())
            {
                var liveContentSize = db.GetLongProperty(context, LiveDataSizeBytes, kvp.Key, kvp.Value);
                var liveFileSize = db.GetLongProperty(context, LiveFileSizeBytes, kvp.Key, kvp.Value);
                var totalFileSize = db.GetLongProperty(context, TotalFileSizeBytes, kvp.Key, kvp.Value);

                result[kvp.Key] = new ColumnFamilyStats(liveContentSize, liveFileSize, totalFileSize);
            }

            return result;
        }

        private static long? GetLongProperty(this RocksDb db, OperationContext context, string propertyName, string columnFamilyName, ColumnFamilyHandle cfh)
        {
            try
            {
                return long.Parse(db.GetProperty(propertyName, cfh));
            }
            catch (Exception exception)
            {
                Tracer.Warning(context, exception, $"Error retrieving or parsing property '{propertyName}' for column '{columnFamilyName}'.");
                return null;
            }
        }
    }
}
