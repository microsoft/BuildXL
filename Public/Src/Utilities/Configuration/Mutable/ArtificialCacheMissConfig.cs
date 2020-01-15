// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class ArtificialCacheMissConfig : IArtificialCacheMissConfig
    {
        /// <nodoc />
        public ArtificialCacheMissConfig()
        {
        }

        /// <nodoc />
        public ArtificialCacheMissConfig(IArtificialCacheMissConfig template)
        {
            Contract.Assume(template != null);

            Seed = template.Seed;
            Rate = template.Rate;
            IsInverted = template.IsInverted;
        }

        /// <inheritdoc />
        public int Seed { get; set; }

        /// <inheritdoc />
        public ushort Rate { get; set; }

        /// <inheritdoc />
        public bool IsInverted { get; set; }
    }
}
