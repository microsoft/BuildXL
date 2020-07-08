// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
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
        };

        public static void Threshold<T>(T value, Thresholds<T> thresholds, Action<Severity, T> action, IComparer<T>? comparer = null)
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
                action(severity.Value, threshold.Value);
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
