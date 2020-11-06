// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

#nullable enable

namespace BuildXL.Cache.ContentStore.UtilitiesCore.Sketching
{
    /// <summary>
    /// Implements a quantile sketching technique, based on DDSketch. For details, read
    /// http://www.vldb.org/pvldb/vol12/p2195-masson.pdf .
    /// 
    /// A reasonably good summary is available at https://blog.acolyer.org/2019/09/06/ddsketch/ .
    ///
    /// This sketch was chosen because it has quite a few good properties:
    ///  1. Algorithm is extremely simple to understand
    ///  2. Sketch size is low as the number of elements grows
    ///  3. The maximum error possible is predictably bounded, and memory trade-off predictable
    ///  5. Very fast, easy to code, easy to use
    ///
    /// When you use this sketch, you need to setup a backing store. It has to implement the methods at
    /// <see cref="DDSketchStore"/>. The memory usage and accuracy of the sketch depends intimately of the store used.
    ///
    /// Since DDSketch can only handle positive values, the implementation here does mirroring to be able to use
    /// negative values as well.
    /// </summary>
    /// <remarks>
    /// A brief summary of the idea behind DDSketch follows.
    ///
    /// Imagine you have a bunch of data points X_1, ..., X_K \in [0, 10]. If you wanted to compute quantiles, what you
    /// can do is bucketize [0, 10] every say 0.01. Then, when you receive X_i, you compute I_i = ceil(X_i/0.01), the
    /// bucket index. At that point, you add one to buckets[I_i].
    ///
    /// At the end, buckets[i] will count the number of values you have seen between i*0.01 and (i + 1)*0.01. Say you
    /// have seen K elements total thus far. If you were looking for the 50th quantile, you'd be looking for the value
    /// x such that you have accumulated 0.5*K elements. You can obtain this by summing from 0 to K and breaking as
    /// soon as you accumulate over that amount. That'd get you an index I, which you can then translate to an element
    /// in the range x = 0.01 * I.
    ///
    /// That's basically what this sketch does. The only difference is that the buckets are exponentially increasing in
    /// size instead of linear.
    /// </remarks>
    public class DDSketch
    {
        /// <summary>
        /// Roughly represents "percentual accuracy". Specifically, the maximum error between the actual quantile x_q
        /// and its sketch approximation x_q' is:
        ///   | x_q' - x_q | \leq | alpha * x_q |
        ///
        /// Since the sketch's guarantee is relative w.r.t. the quantile size, what alpha should be depends on the
        /// range of the variable you want to sketch. For example, you can pick alpha by ensuring that
        /// alpha * (max value) is less than the maximum error you're willing to have, and this guarantees an error
        /// bound.
        /// </summary>
        public double Alpha { get; private set; }

        /// <summary>
        /// Gamma is the bucket size. It is derived from Alpha such that it guarantees the rank accuracy holds.
        /// </summary>
        public double Gamma { get; private set; }

        /// <summary>
        /// \ln{\gamma}. Used to compute \log_{\gamma}(x)
        /// </summary>
        /// <remarks>
        /// This is pre-computed because otherwise we'll have to do it very often.
        /// </remarks>
        private readonly double _ln_gamma;

        /// <summary>
        /// Minimum absolute value the sketch can track. If an element has absolute value less than this number, it
        /// gets "collapsed" into 0. See <see cref="GetBucketIndex(double)"/>
        /// </summary>
        private readonly double _minimumRepresentableValue;

        /// <summary>
        /// The index of <see cref="_minimumRepresentableValue"/>
        /// </summary>
        private readonly int _minimumRepresentableOffset;

        private readonly DDSketchStore _store;

        /// <nodoc />
        public ulong Count { get; private set; } = 0;

        /// <nodoc />
        public double Sum { get; private set; } = 0;

        /// <nodoc />
        public double Average => Sum / Count;

        /// <nodoc />
        public double Min { get; private set; } = double.PositiveInfinity;

        /// <nodoc />
        public double Max { get; private set; } = double.NegativeInfinity;

        /// <summary>
        /// ln(1+x)
        /// 
        /// When x is very small, this is numerically unstable, and not in C#'s Math library. This uses the Taylor
        /// approximation for very small x.
        ///
        /// See: https://www.johndcook.com/blog/cpp_log_one_plus_x/
        /// </summary>
        private static double Log1p(double x) => Math.Abs(x) > 1e-4 ? Math.Log(1.0 + x) : (-0.5 * x + 1.0) * x;

        /// <nodoc />
        public DDSketch(double alpha = 0.01, double minimumRepresentableValue = 1e-9, DDSketchStore? store = null)
        {
            Contract.Requires(0 < alpha && alpha < 1, "Alpha must be a probability");
            Contract.Requires(minimumRepresentableValue >= 0, "The minimum absolute value must be at least 0");

            Alpha = alpha;
            _minimumRepresentableValue = minimumRepresentableValue;

            Gamma = 1.0 + ((2.0 * Alpha) / (1.0 - Alpha));
            Contract.Assert(Gamma >= 1.0);

            // \gamma = 1 + \frac{2 \alpha}{1 - \alpha}, so this is exactly \ln \gamma
            _ln_gamma = Log1p((2.0 * Alpha) / (1.0 - Alpha));

            _minimumRepresentableOffset = -ObliviousIndex(_minimumRepresentableValue) + 1;
            Contract.Assert(_minimumRepresentableOffset > 0);

            _store = store ?? new DDSketchUnboundedStore();
        }

        private double LogGamma(double x) => Math.Log(x) / _ln_gamma;

        private int ObliviousIndex(double element)
        {
            Contract.Requires(element > 0);
            return (int)Math.Ceiling(LogGamma(element));
        }

        /// <summary>
        /// Obtains the bucket index for a given element, accounting for the collapse that happens around small values
        /// </summary>
        private int GetBucketIndex(double element)
        {
            if (element < -_minimumRepresentableValue)
            {
                return -ObliviousIndex(-element) - _minimumRepresentableOffset;
            }

            if (element > _minimumRepresentableValue)
            {
                return ObliviousIndex(element) + _minimumRepresentableOffset;
            }

            return 0;
        }

        /// <nodoc />
        public void Insert(double element)
        {
            _store.Add(GetBucketIndex(element));

            ++Count;
            Sum += element;
            Min = Math.Min(Min, element);
            Max = Math.Max(Max, element);
        }

        /// <nodoc />
        public double Quantile(double quantile)
        {
            Contract.Requires(0 <= quantile && quantile <= 1, "Quantile must be a probability");
            Contract.Requires(Count > 0, "Can't estimate quantiles of an empty stream");

            if (quantile == 0)
            {
                return Min;
            }

            if (quantile == 1)
            {
                return Max;
            }


            // By default (i.e. when the index is 0), the estimate is set to 0. Here's where the "collapse" happens.
            double estimate = 0;

            // Obtains the bucket index at which we have accumulated ~qN elements
            // TODO(jubayard): this can be optimized; if the quantile > 0.5, it is faster to start at the end or the
            // middle (depends on page boundaries)
            var index = _store.IndexOf(rank: (int)Math.Floor(quantile * (Count - 1) + 1));

            // We use one sketch for each side of the real line. Treatment is mirrored.
            if (index < 0)
            {
                estimate = (-2.0 * Math.Pow(Gamma, -index - _minimumRepresentableOffset)) / (1 + Gamma);
            }
            else if (index > 0)
            {
                estimate = (2.0 * Math.Pow(Gamma, index - _minimumRepresentableOffset)) / (1 + Gamma);
            }
            else
            {
                estimate = 0;
            }

            // Check that the returned value is larger than the minimum since for q close to 0 (key in the smallest
            // bin) the midpoint of the bin boundaries could be smaller than the minimum.
            if (estimate > Max)
            {
                return Max;
            }

            if (estimate < Min)
            {
                return Min;
            }

            return estimate;
        }

        /// <nodoc />
        public void Merge(DDSketch other)
        {
            Contract.Requires(Gamma == other.Gamma && _minimumRepresentableValue == other._minimumRepresentableValue,
                "Sketches can only be merged if their gamma and minimum value are equal");

            if (other.Count == 0)
            {
                // Nothing to merge from the other sketch
                return;
            }

            if (Count == 0)
            {
                // Nothing to merge from this one, just replace everything.
                Copy(other);
                return;
            }

            _store.Merge(other._store);
            Count += other.Count;
            Sum += other.Sum;
            Min = Math.Min(Min, other.Min);
            Max = Math.Max(Max, other.Max);
        }

        private void Copy(DDSketch other)
        {
            _store.Copy(other._store);
            Count = other.Count;
            Sum = other.Sum;
            Min = other.Min;
            Max = other.Max;
        }
    }
}
