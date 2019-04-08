// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// Base class for drop-related events.
    /// </summary>
    public abstract class DropOperationBaseEvent : CloudBuildEvent
    {
        #region outcome

        /// <summary>
        /// Whether the create operation succeeded.
        /// </summary>
        public bool Succeeded { get; set; }
        #endregion

        #region user-friendly messages

        /// <summary>
        /// Holds a non-empty string IFF <see cref="Succeeded"/> equals to <code>false</code>.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Arbitrary additional information.
        /// </summary>
        public string AdditionalInformation { get; set; }
        #endregion

        #region statistics

        /// <summary>
        /// Duration of the "drop create" operation.
        /// </summary>
        public long ElapsedTimeTicks { get; set; }
        #endregion

        #region drop information

        /// <summary>
        /// Type of the drop. BuildXL currently supports "VsoDrop" only.
        /// </summary>
        public string DropType { get; set; }

        /// <summary>
        /// URL where the drop can be viewed/downloaded in case of a successful creation.
        /// </summary>
        public string DropUrl { get; set; }
        #endregion
    }
}
