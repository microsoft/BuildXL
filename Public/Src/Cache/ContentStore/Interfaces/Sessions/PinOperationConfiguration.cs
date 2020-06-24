// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
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

        /// <nodoc />
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Default configuration for pins.
        /// </summary>
        public static PinOperationConfiguration Default()
        {
            return new PinOperationConfiguration()
            {
                ReturnGlobalExistenceFast = false,
                UrgencyHint = UrgencyHint.Nominal,
                CancellationToken = CancellationToken.None,
            };
        }
    }
}
