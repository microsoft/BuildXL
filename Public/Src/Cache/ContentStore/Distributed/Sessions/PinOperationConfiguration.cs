// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Configuration object for configurable pins.
    /// </summary>
    public class PinOperationConfiguration
    {
        /// <summary>
        /// Pin checks for global existence for content, fire and forget the default pin action, and return the global existence result.
        /// </summary>
        public bool ReturnGlobalExistenceFast { get; set; }

        /// <nodoc />
        public UrgencyHint UrgencyHint { get; set; }

        /// <summary>
        /// Default configuration for pins.
        /// </summary>
        public static PinOperationConfiguration Default()
        {
            return new PinOperationConfiguration()
            {
                ReturnGlobalExistenceFast = false,
                UrgencyHint = UrgencyHint.Nominal
            };
        }
    }
}
