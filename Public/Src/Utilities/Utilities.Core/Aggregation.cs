// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// An aggregation of performance data
    /// </summary>
    public sealed class Aggregation
    {
        /// <summary>
        /// Delegate type for the <see cref="OnChange"/> event.
        /// </summary>
        public delegate void OnChangeHandler(Aggregation source);

        /// <summary>
        /// Event that is fired every time this aggregation is updated.
        /// </summary>
        public event OnChangeHandler OnChange;

        /// <summary>
        /// The value of the first sample
        /// </summary>
        public double First { get; private set; }

        /// <summary>
        /// The value of the latest sample
        /// </summary>
        public double Latest { get; private set; }

        /// <summary>
        /// The value of the minimum sample
        /// </summary>
        public double Minimum { get; private set; }

        /// <summary>
        /// The value of the maximum sample
        /// </summary>
        public double Maximum { get; private set; }

        /// <summary>
        /// The difference between the currrent value and the previous value
        /// </summary>
        public double Difference { get; private set; }

        /// <summary>
        /// The difference between the first value and the last value
        /// </summary>
        public double Range => Latest - First;

        /// <summary>
        /// The value of all samples
        /// </summary>
        public double Total { get; private set; }

        /// <summary>
        /// The number of samples collected
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// The time in UTC when the first sample was collected
        /// </summary>
        public DateTime FirstSampleTime = DateTime.MinValue;

        /// <summary>
        /// The time in UTC when the latest sample was collected
        /// </summary>
        public DateTime LatestSampleTime = DateTime.MinValue;

        /// <summary>
        /// The average value of all samples collected
        /// </summary>
        public double Average => Count == 0 ? 0 : Total / Count;

        /// <summary>
        /// Registers a new sample
        /// </summary>
        public void RegisterSample(double? sample)
        {
            if (sample.HasValue && !double.IsNaN(sample.Value))
            {
                Difference = sample.Value - Latest;
                Latest = sample.Value;
                LatestSampleTime = DateTime.UtcNow;

                if (Count == 0)
                {
                    First = Latest;
                    FirstSampleTime = DateTime.UtcNow;
                    Maximum = Latest;
                    Minimum = Latest;
                }

                if (Maximum < Latest)
                {
                    Maximum = Latest;
                }

                if (Minimum > Latest)
                {
                    Minimum = Latest;
                }

                Total += Latest;
                Count++;

                OnChange?.Invoke(this);
            }
        }
    }
}
