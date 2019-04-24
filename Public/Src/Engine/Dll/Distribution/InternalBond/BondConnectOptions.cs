// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Options to use when initializing a bond client
    /// </summary>
    /// <remarks>
    /// These utilities were imported from BuildCache to handle dependency issues
    /// </remarks>
    public sealed class BondConnectOptions
    {
        /// <summary>
        /// Options to use when initializing a bond client
        /// </summary>
        /// <param name="reconnectAutomatically">Whether to let bond to re-establish a connection automatically</param>
        /// <param name="timeoutInMs">Timeout in milliseconds to wait for a connection to be established</param>
        public BondConnectOptions(bool reconnectAutomatically, uint timeoutInMs)
        {
            ReconnectAutomatically = reconnectAutomatically;
            TimeoutInMs = timeoutInMs;
        }

        /// <summary>
        /// Default connection options for a bond connection
        /// </summary>
        public static readonly BondConnectOptions Default = new BondConnectOptions(true, 30000);

        /// <summary>
        /// Whether to let bond to re-establish a connection automatically
        /// </summary>
        public bool ReconnectAutomatically { get; private set; }

        /// <summary>
        /// Timeout in milliseconds to wait for a connection to be established
        /// </summary>
        public uint TimeoutInMs { get; private set; }
    }
}
#endif
