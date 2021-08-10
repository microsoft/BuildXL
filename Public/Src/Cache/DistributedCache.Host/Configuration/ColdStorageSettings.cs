// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public class ColdStorageSettings
    {

        [DataMember]
        public string? CacheRootPath { get; set; }

        [DataMember]
        public string? CacheSizeQuotaString { get; set; }

        # region ContentStoreSettings

        [DataMember]
        public bool CheckLocalFiles { get; set; } = false;

        [DataMember]
        public bool OverrideUnixFileAccessMode { get; set; } = false;

        [DataMember]
        public bool UseRedundantPutFileShortcut { get; set; } = false;

        [DataMember]
        public bool TraceFileSystemContentStoreDiagnosticMessages { get; set; } = false;

        [DataMember]
        public bool UseFastHibernationPin { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SilentOperationDurationThreshold { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? DefaultPendingOperationTracingIntervalInMinutes { get; set; }

        [DataMember]
        [Validation.Range(0, double.MaxValue, minInclusive: false)]
        public double? ReserveSpaceTimeoutInMinutes { get; set; }

        [DataMember]
        public bool? UseAsynchronousFileStreamOptionByDefault { get; set; }

        [DataMember]
        public bool? UseHierarchicalTraceIds { get; set; }

        [DataMember]
        public string SelfCheckEpoch { get; set; } = "E0";

        [DataMember]
        public bool StartSelfCheckAtStartup { get; set; } = false;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int SelfCheckFrequencyInMinutes { get; set; } = (int)System.TimeSpan.FromDays(1).TotalMinutes;

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SelfCheckProgressReportingIntervalInMinutes { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SelfCheckDelayInMilliseconds { get; set; }

        [DataMember]
        [Validation.Range(1, int.MaxValue)]
        public int? SelfCheckDefaultHddDelayInMilliseconds { get; set; }

        # endregion

        public ColdStorageSettings() { }

        public ColdStorageSettings(string cacheRootPath, string cacheSizeQuotaString)
        {
            CacheRootPath = cacheRootPath;
            CacheSizeQuotaString = cacheSizeQuotaString;
        }

        public AbsolutePath GetAbsoulutePath() {
            return new AbsolutePath(CacheRootPath!);
        }

     }
}
