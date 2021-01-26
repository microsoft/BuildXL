// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;

namespace BuildXL.Cache.Monitor.Library.Rules.Autoscaling
{
    public class RedisTier : IEquatable<RedisTier>
    {
        public class RedisTierProperties
        {
            public int MemorySizeMb { get; internal set; }

            public double MonthlyCostPerShardUsd { get; internal set; }

            /// <summary>
            /// The number of cores per shard is a relatively useless metric. Redis is a single-threaded server, with
            /// concurrent handling of IO operations. There are some background processes that do run in different
            /// threads, but these don't seem to be important enough to make a significant difference.
            /// </summary>
            public int NumCoresPerShard { get; internal set; }

            public int? AvailableBandwidthMb { get; internal set; }

            public int? EstimatedRequestsPerSecond { get; internal set; }
        }

        private static Dictionary<(RedisPlan, int), RedisTierProperties> TierProperties { get; } =
            new Dictionary<(RedisPlan, int), RedisTierProperties>()
            {
                {
                    (RedisPlan.Basic, 0), new RedisTierProperties
                    {
                        MemorySizeMb = 250,
                        MonthlyCostPerShardUsd = 16.37,
                        NumCoresPerShard = 0,
                    }
                },
                {
                    (RedisPlan.Basic, 1), new RedisTierProperties
                    {
                        MemorySizeMb = 1_000,
                        MonthlyCostPerShardUsd = 40.92,
                        NumCoresPerShard = 1,
                    }
                },
                {
                    (RedisPlan.Basic, 2), new RedisTierProperties
                    {
                        MemorySizeMb = 2_500,
                        MonthlyCostPerShardUsd = 66.96,
                        NumCoresPerShard = 2,
                    }
                },
                {
                    (RedisPlan.Basic, 3), new RedisTierProperties
                    {
                        MemorySizeMb = 6_000,
                        MonthlyCostPerShardUsd = 133.92,
                        NumCoresPerShard = 4,
                    }
                },
                {
                    (RedisPlan.Basic, 4), new RedisTierProperties
                    {
                        MemorySizeMb = 13_000,
                        MonthlyCostPerShardUsd = 156.24,
                        NumCoresPerShard = 2,
                    }
                },
                {
                    (RedisPlan.Basic, 5), new RedisTierProperties
                    {
                        MemorySizeMb = 26_000,
                        MonthlyCostPerShardUsd = 312.48,
                        NumCoresPerShard = 4,
                    }
                },
                {
                    (RedisPlan.Basic, 6), new RedisTierProperties
                    {
                        MemorySizeMb = 53_000,
                        MonthlyCostPerShardUsd = 624.96,
                        NumCoresPerShard = 8,
                    }
                },
                {
                    (RedisPlan.Standard, 0), new RedisTierProperties
                    {
                        MemorySizeMb = 250,
                        MonthlyCostPerShardUsd = 40.92,
                        NumCoresPerShard = 0,
                        AvailableBandwidthMb = 100,
                        EstimatedRequestsPerSecond = 7_500,
                    }
                },
                {
                    (RedisPlan.Standard, 1), new RedisTierProperties
                    {
                        MemorySizeMb = 1_000,
                        MonthlyCostPerShardUsd = 102.67,
                        NumCoresPerShard = 1,
                        AvailableBandwidthMb = 500,
                        EstimatedRequestsPerSecond = 20_720,
                    }
                },
                {
                    (RedisPlan.Standard, 2), new RedisTierProperties
                    {
                        MemorySizeMb = 2_500,
                        MonthlyCostPerShardUsd = 166.66,
                        NumCoresPerShard = 2,
                        AvailableBandwidthMb = 500,
                        EstimatedRequestsPerSecond = 37_000,
                    }
                },
                {
                    (RedisPlan.Standard, 3), new RedisTierProperties
                    {
                        MemorySizeMb = 6_000,
                        MonthlyCostPerShardUsd = 334.80,
                        NumCoresPerShard = 4,
                        AvailableBandwidthMb = 1_000,
                        EstimatedRequestsPerSecond = 90_000,
                    }
                },
                {
                    (RedisPlan.Standard, 4), new RedisTierProperties
                    {
                        MemorySizeMb = 13_000,
                        MonthlyCostPerShardUsd = 389.86,
                        NumCoresPerShard = 2,
                        AvailableBandwidthMb = 500,
                        EstimatedRequestsPerSecond = 55_000,
                    }
                },
                {
                    (RedisPlan.Standard, 5), new RedisTierProperties
                    {
                        MemorySizeMb = 26_000,
                        MonthlyCostPerShardUsd = 781.20,
                        NumCoresPerShard = 4,
                        AvailableBandwidthMb = 1_000,
                        EstimatedRequestsPerSecond = 93_000,
                    }
                },
                {
                    (RedisPlan.Standard, 6), new RedisTierProperties
                    {
                        MemorySizeMb = 53_000,
                        MonthlyCostPerShardUsd = 1562.40,
                        NumCoresPerShard = 8,
                        AvailableBandwidthMb = 2_000,
                        EstimatedRequestsPerSecond = 172_000,
                    }
                },
                {
                    (RedisPlan.Premium, 1), new RedisTierProperties
                    {
                        MemorySizeMb = 6_000,
                        MonthlyCostPerShardUsd = 412.18,
                        NumCoresPerShard = 2,
                        AvailableBandwidthMb = 1500,
                        EstimatedRequestsPerSecond = 172_000,
                    }
                },
                {
                    (RedisPlan.Premium, 2), new RedisTierProperties
                    {
                        MemorySizeMb = 13_000,
                        MonthlyCostPerShardUsd = 825.84,
                        NumCoresPerShard = 4,
                        AvailableBandwidthMb = 3000,
                        EstimatedRequestsPerSecond = 341_000,
                    }
                },
                {
                    (RedisPlan.Premium, 3), new RedisTierProperties
                    {
                        MemorySizeMb = 26_000,
                        MonthlyCostPerShardUsd = 1650.19,
                        NumCoresPerShard = 4,
                        AvailableBandwidthMb = 3000,
                        EstimatedRequestsPerSecond = 341_000,
                    }
                },
                {
                    (RedisPlan.Premium, 4), new RedisTierProperties
                    {
                        MemorySizeMb = 53_000,
                        MonthlyCostPerShardUsd = 3303.36,
                        NumCoresPerShard = 8,
                        AvailableBandwidthMb = 6000,
                        EstimatedRequestsPerSecond = 373_000,
                    }
                },
                {
                    (RedisPlan.Premium, 5), new RedisTierProperties
                    {
                        MemorySizeMb = 120_000,
                        MonthlyCostPerShardUsd = 7477.20,
                        NumCoresPerShard = 20,
                        AvailableBandwidthMb = 6000,
                        EstimatedRequestsPerSecond = 373_000,
                    }
                },
            };

        public static IReadOnlyList<RedisTier> Instances { get; } = TierProperties.Select(kvp => new RedisTier(kvp.Key.Item1, kvp.Key.Item2)).ToArray();

        public RedisPlan Plan { get; }

        /// <summary>
        /// For basic and standard, from 0 through 6 inclusive. For Premium, from 1 through 5 inclusive
        /// </summary>
        public int Capacity { get; }

        public RedisTierProperties Properties => TierProperties[(Plan, Capacity)];

        public RedisTier(RedisPlan plan, int capacity)
        {
            Contract.Requires(TierProperties.ContainsKey((plan, capacity)), "Unknown Azure Cache for Redis tier");
            Plan = plan;
            Capacity = capacity;
        }

        public static Result<RedisTier> TryParse(string redisTier)
        {
            if (string.IsNullOrEmpty(redisTier))
            {
                return new Result<RedisTier>(errorMessage: $"Empty string can't be parsed into {nameof(RedisTier)}");
            }

            var plan = RedisPlan.Basic;
            switch (redisTier[0])
            {
                case 'B':
                    plan = RedisPlan.Basic;
                    break;
                case 'S':
                    plan = RedisPlan.Standard;
                    break;
                case 'P':
                    plan = RedisPlan.Premium;
                    break;
                default:
                    return new Result<RedisTier>(errorMessage: $"Could not parse {nameof(RedisPlan)} from `{redisTier[0]}`");
            }

            var capacityString = redisTier.Substring(1);
            if (!int.TryParse(capacityString, out var capacity))
            {
                return new Result<RedisTier>(errorMessage: $"Could not parse capacity from `{capacityString}`");
            }

            if (!TierProperties.ContainsKey((plan, capacity)))
            {
                return new Result<RedisTier>(errorMessage: $"Tier `{plan}{capacity}` is invalid");
            }

            return new RedisTier(plan, capacity);
        }

        public override string ToString()
        {
            var planShortHand = "U";
            switch (Plan)
            {
                case RedisPlan.Basic:
                    planShortHand = "B";
                    break;
                case RedisPlan.Standard:
                    planShortHand = "S";
                    break;
                case RedisPlan.Premium:
                    planShortHand = "P";
                    break;
            }

            return $"{planShortHand}{Capacity}";
        }

        public override bool Equals(object? obj)
        {
            return obj is RedisTier item && Equals(item);
        }

        public bool Equals(RedisTier? redisTier)
        {
            return redisTier != null && Plan == redisTier.Plan && Capacity == redisTier.Capacity;
        }

        public override int GetHashCode() => (Plan, Capacity).GetHashCode();
    }
}
