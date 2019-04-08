// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Runtime.Serialization;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Common definitions for a content quota.
    /// </summary>
    [DataContract]
    public class ContentStoreQuota
    {
        /// <summary>
        ///     Public way to specify hard limit in JSON.
        /// </summary>
        /// <remarks>
        ///     Simple number units are recognized. For instance, this can be "1GB".
        /// </remarks>
        [DataMember(Name = "Hard")]
        protected readonly string HardExpression;

        /// <summary>
        ///     Public way to specify soft limit in JSON.
        /// </summary>
        /// <remarks>
        ///     Simple number units are recognized. For instance, this can be "1GB".
        /// </remarks>
        [DataMember(Name = "Soft")]
        protected readonly string SoftExpression;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentStoreQuota"/> class.
        /// </summary>
        protected ContentStoreQuota(string hardExpression, string softExpression)
        {
            HardExpression = hardExpression;
            SoftExpression = softExpression;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentStoreQuota"/> class.
        /// </summary>
        protected ContentStoreQuota(string expression)
        {
            Contract.Requires(expression != null);

            var tuple = expression.ExtractHardSoft();
            HardExpression = tuple.Item1;
            SoftExpression = tuple.Item2;
        }

        /// <summary>
        ///     Gets or sets a value indicating whether the state is valid after deserialization.
        /// </summary>
        /// <remarks>
        ///     This value should be checked before attempting to read the limit property values.
        ///     If this property is false, the limit property values are not valid.
        /// </remarks>
        public bool IsValid { get; protected set; }

        /// <summary>
        ///     Gets or sets a descriptive error when IsValid gives false.
        /// </summary>
        public string Error { get; protected set; }

        /// <summary>
        ///     Gets or sets absolute limit which blocks puts until space available.
        /// </summary>
        /// <remarks>
        ///     Attempts to add new content are blocked until the background purger makes enough room for the new content.
        /// </remarks>
        public long Hard { get; protected set; }

        /// <summary>
        ///     Gets or sets limit which activates background purging.
        /// </summary>
        /// <remarks>
        ///     Once this threshold is hit, background purging will be activated. Attempts to add new content are not throttled while size
        ///     remains below the HardLimit.
        /// </remarks>
        public long Soft { get; protected set; }

        /// <summary>
        ///     Gets or sets target value purging will aim for once activated.
        /// </summary>
        /// <remarks>
        ///     This will be offset from Soft somewhat to achieve hysteresis, limiting purger chattering around the soft threshold.
        /// </remarks>
        public long Target { get; protected set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return IsValid
                ? $"{nameof(Hard)}=[{Hard}], {nameof(Soft)}=[{Soft}], {nameof(Target)}=[{Target}]"
                : "<INVALID>";
        }
    }
}
