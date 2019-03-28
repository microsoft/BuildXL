// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Resolved, validated, EffectiveContentStoreQuota for MaxSize
    /// </summary>
    [DataContract]
    public class MaxSizeQuota : ContentStoreQuota
    {
        private const long SoftDefaultPercentOffset = 10;
        private const string MaxSizeHardName = "MaxSize.Hard";
        private const string MaxSizeSoftName = "MaxSize.Soft";

        /// <summary>
        ///     Initializes a new instance of the <see cref="MaxSizeQuota"/> class.
        /// </summary>
        public MaxSizeQuota(string hardExpression, string softExpression)
            : base(hardExpression, softExpression)
        {
            Initialize();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MaxSizeQuota"/> class.
        /// </summary>
        public MaxSizeQuota(string expression)
            : base(expression)
        {
            Contract.Requires(expression != null);

            Initialize();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MaxSizeQuota"/> class.
        /// </summary>
        public MaxSizeQuota(long hard)
            : this(hard.ToString())
        {
            Contract.Requires(hard > 0);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MaxSizeQuota"/> class.
        /// </summary>
        public MaxSizeQuota(long hard, long soft)
            : this(hard.ToString(), soft.ToString())
        {
            Contract.Requires(hard > 0);
            Contract.Requires(soft > 0);
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
                throw new CacheException($"{MaxSizeHardName} must be provided");
            }

            try
            {
                Hard = HardExpression.ToSize();

                if (SoftExpression != null)
                {
                    Soft = SoftExpression.ToSize();
                }
                else
                {
                    Soft = Hard - (Hard / SoftDefaultPercentOffset);
                }

                Target = Soft - Math.Max((Hard - Soft) / SoftDefaultPercentOffset, 1);
            }
            catch (ArgumentException exception)
            {
                throw new CacheException("Failed to parse limit expression", exception);
            }

            if (!(Soft < Hard))
            {
                throw new CacheException($"{MaxSizeSoftName}=[{Soft}] must be < {MaxSizeHardName}=[{Hard}]");
            }

            IsValid = true;
        }
    }
}
