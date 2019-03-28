// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Exceptions;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Resolved, validated, EffectiveContentStoreQuota for DiskFreePercent
    /// </summary>
    [DataContract]
    public class DiskFreePercentQuota : ContentStoreQuota
    {
        private const long HardMin = 0;
        private const long HardDefault = 10;
        private const long HardMax = 100;
        private const long SoftMin = 0;
        private const long SoftDefault = 20;
        private const long SoftDefaultOffset = 10;
        private const long SoftMax = 100;
        private const long TargetDefault = 21;
        private const long TargetMax = 100;
        private const string HardName = "DiskFreePercent.Hard";
        private const string SoftName = "DiskFreePercent.Soft";

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiskFreePercentQuota"/> class.
        /// </summary>
        public DiskFreePercentQuota()
            : base(HardDefault.ToString(), SoftDefault.ToString())
        {
            Hard = HardDefault;
            Soft = SoftDefault;
            Target = TargetDefault;
            IsValid = true;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiskFreePercentQuota"/> class.
        /// </summary>
        public DiskFreePercentQuota(string hardExpression, string softExpression)
            : base(hardExpression, softExpression)
        {
            Initialize();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiskFreePercentQuota"/> class.
        /// </summary>
        public DiskFreePercentQuota(string expression)
            : base(expression)
        {
            Contract.Requires(expression != null);

            Initialize();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiskFreePercentQuota"/> class.
        /// </summary>
        public DiskFreePercentQuota(long hard)
            : this(hard.ToString())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiskFreePercentQuota"/> class.
        /// </summary>
        public DiskFreePercentQuota(long hard, long soft)
            : this(hard.ToString(), soft.ToString())
        {
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
            catch (CacheException exception)
            {
                Error = exception.ToString();
                IsValid = false;
            }
        }

        private void Initialize()
        {
            if (HardExpression == null)
            {
                throw new CacheException($"{HardName} must be provided");
            }

            long hard;
            if (!long.TryParse(HardExpression, out hard))
            {
                throw new CacheException($"{HardName}=[{HardExpression}] cannot be parsed as a positive number");
            }

            long soft;
            if (SoftExpression != null)
            {
                if (!long.TryParse(SoftExpression, out soft))
                {
                    throw new CacheException($"{SoftName}=[{SoftExpression}] cannot be parsed as a positive number");
                }
            }
            else
            {
                soft = Math.Min(hard + SoftDefaultOffset, SoftMax);
            }

            if (hard < HardMin || hard > HardMax)
            {
                throw new CacheException($"{HardName}=[{hard}] out of range");
            }

            if (soft < SoftMin || soft > SoftMax)
            {
                throw new CacheException($"{SoftName}=[{soft}] out of range");
            }

            if (!(soft > hard))
            {
                throw new CacheException($"{SoftName}=[{soft}] must be > {HardName}=[{hard}]");
            }

            Hard = hard;
            Soft = soft;
            Target = Math.Min(Soft + Math.Max((Soft - Hard) / 10, 1), TargetMax);

            IsValid = true;
        }
    }
}
