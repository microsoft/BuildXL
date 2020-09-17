using System;
using System.Collections.Generic;
using System.Text;

namespace CopyClient
{
    internal abstract class BandwidthGovener
    {
        public abstract bool IsActive { get; }

        public abstract TimeSpan CheckInterval { get; }

        public abstract double MinimumBytesPerSecond { get; }

        public abstract void Record(long bytes, TimeSpan duration);

    }

    internal class FixedBandwidthGovener : BandwidthGovener
    {
        public override bool IsActive => true;

        public override TimeSpan CheckInterval => TimeSpan.FromSeconds(10.0);

        public override double MinimumBytesPerSecond => 1.0;

        public override void Record(long bytes, TimeSpan duration) { }
    }
}
