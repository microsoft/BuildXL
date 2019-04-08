// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Artifical cache miss opton
    /// </summary>
    public partial interface IArtificialCacheMissConfig
    {
        /// <summary>
        /// Seed determining the pip subset for the configured miss-rate (possibly random from creation).
        /// </summary>
        int Seed { get; }

        /// <summary>
        /// Specified rate in the range [0.0, UShort.Max].
        /// </summary>
        /// <remarks>
        /// Since this is actually a double value, but can't encode doubles in DScript,
        /// we are forced to encode the double as a ushort.
        /// </remarks>
        ushort Rate { get; }

        /// <summary>
        /// Indicates if the ShouldHaveArtificialMiss determination is inverted.
        /// </summary>
        bool IsInverted { get; }
    }
}
