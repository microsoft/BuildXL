using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CopyClient
{
    internal abstract class BandwidthMonitor
    {
        public abstract void Record(double bandwidth);

        public abstract double Read(double rank);

    }

    internal class SimpleBandwidthMonitor : BandwidthMonitor
    {
        public SimpleBandwidthMonitor(double limit)
        {
            this.limit = limit;
        }

        private double limit = 1.0;

        public override double Read(double rank)
        {
            return limit;
        }

        public override void Record(double bandwidth)
        {
            // no op
        }
    }

    internal class AdaptiveBandwidthMonitor : BandwidthMonitor
    {

        private const int n = 1024;
        private double[] records;
        private List<double> previousSet;
        private List<double> currentSet = new List<double>(n / 2);
        private readonly object sync = new object();

        public override double Read(double rank)
        {
            if (records is null) return 1.0;

            int m = (int)Math.Floor(rank * records.Length);
            Debug.Assert(0 <= m && m < records.Length);
            double value = records[m];
            return Math.Max(value, 1.0);
        }

        public override void Record(double bandwidth)
        {
            lock (sync)
            {
                Debug.Assert(currentSet.Count < currentSet.Capacity);
                currentSet.Add(bandwidth);
                if (currentSet.Count >= currentSet.Capacity)
                {
                    Debug.Assert(currentSet.Count == n / 2);
                    if (previousSet is object)
                    {
                        Debug.Assert(previousSet.Count == n / 2);
                        double[] newRecords = new double[n];
                        previousSet.CopyTo(newRecords, 0);
                        currentSet.CopyTo(newRecords, n / 2);
                        Array.Sort<double>(newRecords);
                        records = newRecords;
                    }
                    previousSet = currentSet;
                    currentSet = new List<double>(n / 2);
                    Debug.Assert(currentSet.Count == 0);
                }
            }
        }
    }
}
