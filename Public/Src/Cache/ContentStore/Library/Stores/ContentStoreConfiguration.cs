// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.Serialization;
using System.Text;
using BuildXL.Cache.ContentStore.Exceptions;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     User-facing top-level service configuration, usually from JSON.
    /// </summary>
    [DataContract]
    public class ContentStoreConfiguration
    {
        /// <summary>
        ///     SingleInstanceTimeoutSeconds dDefault value if not specified.
        /// </summary>
        public const int DefaultSingleInstanceTimeoutSeconds = 60 * 30;

        /// <summary>
        ///     Convenience factory method for creating configuration with hard limit in megabytes.
        /// </summary>
        public static ContentStoreConfiguration CreateWithMaxSizeQuotaMB(uint megabytes)
        {
            Contract.Requires(megabytes > 0);
            return new ContentStoreConfiguration(new MaxSizeQuota($"{megabytes}MB"));
        }

        /// <summary>
        ///     Convenience factory method for creating configuration with elastic size.
        /// </summary>
        public static ContentStoreConfiguration CreateWithElasticSize(
            uint? initialElasticSizeMegabytes = default(uint?),
            int? historyBufferSize = default(int?),
            int? historyWindowSize = default(int?))
        {
            return new ContentStoreConfiguration(
                enableElasticity: true,
                initialElasticSize: initialElasticSizeMegabytes.HasValue ? new MaxSizeQuota($"{initialElasticSizeMegabytes}MB") : null,
                historyBufferSize: historyBufferSize,
                historyWindowSize: historyWindowSize);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentStoreConfiguration"/> class.
        /// </summary>
        public ContentStoreConfiguration
            (
            MaxSizeQuota maxSizeQuota = null,
            DiskFreePercentQuota diskFreePercentQuota = null,
            DenyWriteAttributesOnContentSetting denyWriteAttributesOnContent = DenyWriteAttributesOnContentSetting.Disable,
            int singleInstanceTimeoutSeconds = DefaultSingleInstanceTimeoutSeconds,
            bool enableElasticity = false,
            MaxSizeQuota initialElasticSize = null,
            int? historyBufferSize = default(int?),
            int? historyWindowSize = default(int?)
            )
        {
            Contract.Requires(singleInstanceTimeoutSeconds > 0);

            MaxSizeQuota = maxSizeQuota;
            DiskFreePercentQuota = diskFreePercentQuota;
            DenyWriteAttributesOnContent = denyWriteAttributesOnContent;
            SingleInstanceTimeoutSeconds = singleInstanceTimeoutSeconds;
            EnableElasticity = enableElasticity;
            InitialElasticSize = initialElasticSize;
            HistoryBufferSize = historyBufferSize;
            HistoryWindowSize = historyWindowSize;

            Initialize();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentStoreConfiguration"/> class.
        /// </summary>
        public ContentStoreConfiguration
            (
            string maxSizeExpression,
            string diskFreePercentExpression,
            DenyWriteAttributesOnContentSetting denyWriteAttributesOnContent = DenyWriteAttributesOnContentSetting.Disable,
            int singleInstanceTimeoutSeconds = DefaultSingleInstanceTimeoutSeconds,
            bool enableElasticity = false,
            string initialElasticSizeExpression = null,
            int? historyBufferSize = default(int?),
            int? historyWindowSize = default(int?)
            )
        {
            Contract.Requires(singleInstanceTimeoutSeconds > 0);

            MaxSizeQuota = !string.IsNullOrEmpty(maxSizeExpression)
                ? new MaxSizeQuota(maxSizeExpression)
                : null;

            DiskFreePercentQuota = !string.IsNullOrEmpty(diskFreePercentExpression)
                ? new DiskFreePercentQuota(diskFreePercentExpression)
                : null;

            DenyWriteAttributesOnContent = denyWriteAttributesOnContent;
            SingleInstanceTimeoutSeconds = singleInstanceTimeoutSeconds;

            EnableElasticity = enableElasticity;

            InitialElasticSize = !string.IsNullOrEmpty(initialElasticSizeExpression)
                ? new MaxSizeQuota(initialElasticSizeExpression)
                : null;

            HistoryBufferSize = historyBufferSize;
            HistoryWindowSize = historyWindowSize;

            Initialize();
        }

        /// <summary>
        ///     Gets effective MaxSize content quota or null if not in effect.
        /// </summary>
        /// <remarks>
        ///     If enabled (not null), the cache will attempt to keep its size below this absolute size. If only MaxSize is selected,
        ///     linked content will be purged only after all unlinked content is purged.
        /// </remarks>
        [DataMember]
        public MaxSizeQuota MaxSizeQuota { get; private set; }

        /// <summary>
        ///     Gets effective DiskFreePercent content quota or null if not in effect.
        ///     Limit cache size to leave a specified amount of free space on the same volume.
        /// </summary>
        /// <remarks>
        ///     If enabled (not null), the cache will attempt to keep its size such that the current amount of disk
        ///     free space remains below the configured limit.
        /// </remarks>
        [DataMember]
        public DiskFreePercentQuota DiskFreePercentQuota { get; private set; }

        [DataMember(Name = "DenyWriteAttributesOnContent")]
        private string _denyWriteAttributesOnContentSerialized;

        private DenyWriteAttributesOnContentSetting _denyWriteAttributesOnContent;

        /// <summary>
        ///     Gets selected method for protecting content files.
        /// </summary>
        public DenyWriteAttributesOnContentSetting DenyWriteAttributesOnContent
        {
            get
            {
                return _denyWriteAttributesOnContent;
            }

            private set
            {
                _denyWriteAttributesOnContent = value;
                _denyWriteAttributesOnContentSerialized = _denyWriteAttributesOnContent.ToString();
            }
        }

        /// <summary>
        ///     Gets or sets the time to wait for exclusive access to a CAS.
        /// </summary>
        [DataMember(Name = "SingleInstanceTimeoutSeconds")]
        public int SingleInstanceTimeoutSeconds { get; set; }

        /// <summary>
        ///     Gets a value indicating whether or not to enable elasticity.
        /// </summary>
        [DataMember(Name = "EnableElasticity")]
        public bool EnableElasticity { get; private set; }

        /// <summary>
        ///     Gets initial size for elasticity.
        /// </summary>
        [DataMember(Name = "InitialElasticSize")]
        public MaxSizeQuota InitialElasticSize { get; private set; }

        /// <summary>
        ///     Gets history buffer size for elasticity.
        /// </summary>
        [DataMember(Name = "HistoryBufferSize")]
        public int? HistoryBufferSize { get; private set; }

        /// <summary>
        ///     Gets history window size for elasticity.
        /// </summary>
        [DataMember(Name = "HistoryWindowSize")]
        public int? HistoryWindowSize { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the state is valid after deserialization.
        /// </summary>
        /// <remarks>
        ///     This value should be checked before attempting to read the limit property values.
        ///     If this property is false, the limit property values are not valid.
        /// </remarks>
        public bool IsValid
        {
            get
            {
                if (MaxSizeQuota != null && !MaxSizeQuota.IsValid)
                {
                    return false;
                }

                if (DiskFreePercentQuota != null && !DiskFreePercentQuota.IsValid)
                {
                    return false;
                }

                if (DenyWriteAttributesOnContent == DenyWriteAttributesOnContentSetting.None)
                {
                    return false;
                }

                if (SingleInstanceTimeoutSeconds <= 0)
                {
                    return false;
                }

                if (InitialElasticSize != null && !InitialElasticSize.IsValid)
                {
                    return false;
                }

                if (HistoryBufferSize.HasValue && HistoryBufferSize.Value < 0)
                {
                    return false;
                }

                if (HistoryWindowSize.HasValue && HistoryWindowSize.Value < 0)
                {
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        ///     Gets a descriptive error when IsValid gives false.
        /// </summary>
        public string Error
        {
            get
            {
                var sb = new StringBuilder();

                if (MaxSizeQuota != null)
                {
                    sb.Append(MaxSizeQuota.Error);
                }

                if (DiskFreePercentQuota != null)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.Append(DiskFreePercentQuota.Error);
                }

                if (SingleInstanceTimeoutSeconds <= 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.Append($"{nameof(SingleInstanceTimeoutSeconds)} must be a postive number");
                }

                if (InitialElasticSize != null)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.Append(InitialElasticSize.Error);
                }

                if (HistoryBufferSize.HasValue && HistoryBufferSize.Value < 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.Append($"{nameof(HistoryBufferSize)} must be greater or equal to 0");
                }

                if (HistoryWindowSize.HasValue && HistoryWindowSize.Value < 0)
                {
                    if (sb.Length > 0)
                    {
                        sb.AppendLine();
                    }

                    sb.Append($"{nameof(HistoryWindowSize)} must be greater or equal to 0");
                }

                return sb.ToString();
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder();

            if (MaxSizeQuota != null)
            {
                sb.Append($"{nameof(Stores.MaxSizeQuota)}=[{MaxSizeQuota}]");
            }

            if (DiskFreePercentQuota != null)
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append($"{nameof(Stores.DiskFreePercentQuota)}=[{DiskFreePercentQuota}]");
            }

            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append($"{nameof(DenyWriteAttributesOnContent)}={DenyWriteAttributesOnContent}");
            sb.Append($", {nameof(SingleInstanceTimeoutSeconds)}={SingleInstanceTimeoutSeconds}");

            if (EnableElasticity)
            {
                sb.Append($", {nameof(EnableElasticity)}={EnableElasticity}");

                if (InitialElasticSize != null)
                {
                    sb.Append($", {nameof(InitialElasticSize)}=[{InitialElasticSize}]");
                }

                if (HistoryBufferSize.HasValue)
                {
                    sb.Append($", {nameof(HistoryBufferSize)}={HistoryBufferSize.Value}");
                }

                if (HistoryWindowSize.HasValue)
                {
                    sb.Append($", {nameof(HistoryWindowSize)}={HistoryWindowSize.Value}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        ///     Hook method invoked after the object has been deserialized.
        /// </summary>
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            try
            {
                Initialize();
            }
            catch (CacheException)
            {
                // ignored
            }
        }

        private void Initialize()
        {
            // If a quota not explicitly provided, the default is DiskFreePercent=10.
            if (MaxSizeQuota == null && DiskFreePercentQuota == null)
            {
                DiskFreePercentQuota = new DiskFreePercentQuota();
            }

            if (string.IsNullOrEmpty(_denyWriteAttributesOnContentSerialized))
            {
                DenyWriteAttributesOnContent = DenyWriteAttributesOnContentSetting.Disable;
            }
            else
            {
                DenyWriteAttributesOnContentSetting denyWriteAttributesOnContent;
                if (!Enum.TryParse(_denyWriteAttributesOnContentSerialized, out denyWriteAttributesOnContent))
                {
                    throw new CacheException($"{_denyWriteAttributesOnContentSerialized} is an unrecognized value");
                }

                DenyWriteAttributesOnContent = denyWriteAttributesOnContent;
            }

            if (SingleInstanceTimeoutSeconds == 0)
            {
                SingleInstanceTimeoutSeconds = DefaultSingleInstanceTimeoutSeconds;
            }
            else if (SingleInstanceTimeoutSeconds < 0)
            {
                throw new CacheException($"{nameof(SingleInstanceTimeoutSeconds)} must be a positive number if specified");
            }
        }
    }
}
