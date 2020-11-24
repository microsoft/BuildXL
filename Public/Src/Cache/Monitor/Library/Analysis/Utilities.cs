// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;

namespace BuildXL.Cache.Monitor.App.Analysis
{
    internal static class Utilities
    {
        public static IEnumerable<T> Yield<T>(this T item)
        {
            yield return item;
        }

        public static void SplitBy<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate, ICollection<T> trueSet, ICollection<T> falseSet)
        {
            // TODO(jubayard): this function can be split in two cases, find the first index at which the predicate is
            // true, and find all entries for which the predicate is true. Need to evaluate case-by-case.
            foreach (var entry in enumerable)
            {
                if (predicate(entry))
                {
                    trueSet.Add(entry);
                }
                else
                {
                    falseSet.Add(entry);
                }
            }
        }

        public class Thresholds<T>
            where T : struct
        {
            public T? Info { get; set; } = null;

            public T? Warning { get; set; } = null;

            public T? Error { get; set; } = null;

            public T? Fatal { get; set; } = null;

            public void Check(T value, Action<Severity, T> action, IComparer<T>? comparer = null) => Threshold(value, this, action, comparer);
        }

        public static void Threshold<T>(T value, Thresholds<T> thresholds, Action<Severity, T> action, IComparer<T>? comparer = null)
            where T : struct
        {
            Func<Severity, T, Task> func = (sev, threshold) =>
            {
                action(sev, threshold);
                return Task.FromResult(1);
            };

            ThresholdAsync(value, thresholds, func, comparer).GetAwaiter().GetResult();
        }

        public static async Task ThresholdAsync<T>(T value, Thresholds<T> thresholds, Func<Severity, T, Task> action, IComparer<T>? comparer = null)
            where T : struct
        {
            if (comparer == null)
            {
                comparer = Comparer<T>.Default;
            }

            Severity? severity = null;
            T? threshold = null;

            if (thresholds.Info != null && comparer.Compare(value, thresholds.Info.Value) >= 0)
            {
                severity = Severity.Info;
                threshold = thresholds.Info;
            }

            if (thresholds.Warning != null && comparer.Compare(value, thresholds.Warning.Value) >= 0)
            {
                severity = Severity.Warning;
                threshold = thresholds.Warning;
            }

            if (thresholds.Error != null && comparer.Compare(value, thresholds.Error.Value) >= 0)
            {
                severity = Severity.Error;
                threshold = thresholds.Error;
            }

            if (thresholds.Fatal != null && comparer.Compare(value, thresholds.Fatal.Value) >= 0)
            {
                severity = Severity.Fatal;
                threshold = thresholds.Fatal;
            }

            if (severity != null)
            {
                Contract.AssertNotNull(threshold);
                await action(severity.Value, threshold.Value);
            }
        }

        public class IcmThresholds<T>
            where T : struct
        {
            public T? Sev4 { get; set; } = null;

            public T? Sev3 { get; set; } = null;

            public T? Sev2 { get; set; } = null;

            public T? Sev1 { get; set; } = null;

            public Task CheckAsync(T value, Func<int, T, Task> action, IComparer<T>? comparer = null) => ThresholdAsync(value, this, action, comparer);
        }

        public static async Task ThresholdAsync<T>(T value, IcmThresholds<T> thresholds, Func<int, T, Task> action, IComparer<T>? comparer = null)
            where T : struct
        {
            // Convert to regular thresholds to avoid having duplicate logic

            var convertedThresholds = new Thresholds<T>
            {
                Info = thresholds.Sev4,
                Warning = thresholds.Sev3,
                Error = thresholds.Sev2,
                Fatal = thresholds.Sev1
            };

            await ThresholdAsync(value, convertedThresholds, convertedAction, comparer);

            Task convertedAction(Severity sev, T value)
            {
                var icmSev = sev switch
                {
                    Severity.Info => 4,
                    Severity.Warning => 3,
                    Severity.Error => 2,
                    Severity.Fatal => 1,
                    _ => throw new NotImplementedException()
                };

                return action(icmSev, value);
            }
        }

        public static void BiThreshold<T, Y>(T left, Y right, Thresholds<T> leftThresholds, Thresholds<Y> rightThresholds, Action<Severity, T?, Y?> action, IComparer<T>? leftComparer = null, IComparer<Y>? rightComparer = null, Func<Severity?, Severity?, Severity?>? merge = null)
            where T : struct
            where Y : struct
        {
            if (merge == null)
            {
                merge = (s1, s2) =>
                {
                    if (s1 == null)
                    {
                        return s2;
                    }

                    if (s2 == null)
                    {
                        return s1;
                    }

                    return (Severity)Math.Max((int)s1, (int)s2);
                };
            }

            Severity? leftSeverity = null;
            T? leftThreshold = null;
            Threshold(left, leftThresholds, (severity, threshold) =>
            {
                leftSeverity = severity;
                leftThreshold = threshold;
            }, leftComparer);

            Severity? rightSeverity = null;
            Y? rightThreshold = null;
            Threshold(right, rightThresholds, (severity, threshold) =>
            {
                rightSeverity = severity;
                rightThreshold = threshold;
            }, rightComparer);

            var finalSeverity = merge(leftSeverity, rightSeverity);
            if (finalSeverity != null)
            {
                action(finalSeverity.Value, leftThreshold, rightThreshold);
            }
        }
    }
}
