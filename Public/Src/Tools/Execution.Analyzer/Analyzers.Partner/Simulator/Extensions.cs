// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Scheduler.Graph;

namespace BuildXL.Execution.Analyzer.Analyzers.Simulator
{
    internal static class Extensions
    {
        public static T GetOrDefault<T>(this ConcurrentNodeDictionary<T> d, NodeId node)
        {
            if (!node.IsValid)
            {
                return default;
            }

            return d[node];
        }
        public static string Format(this string s, string format, params object[] args)
        {
            object[] resultArgs = new object[args.Length + 1];
            resultArgs[0] = s;
            Array.Copy(args, 1, resultArgs, 0, args.Length);

            return string.Format(format, resultArgs);
        }

        public static string FormatWith(this string format, params object[] args)
        {
            return string.Format(format, args);
        }

        public static double ToSeconds(this ulong time)
        {
            return Math.Round(TimeSpan.FromTicks((long)time).TotalSeconds, 2);
        }

        public static double ToMinutes(this ulong time)
        {
            return Math.Round(TimeSpan.FromTicks((long)time).TotalMinutes, 3);
        }

        public static void CompareExchangeMax<T>(this ConcurrentNodeDictionary<T> map, NodeId node, T comparand) where T : IComparable<T>
        {
            if (comparand.IsGreaterThan(map[node]))
            {
                map[node] = comparand;
            }
        }

        public static void CompareExchangeMin<T>(this ConcurrentNodeDictionary<T> map, NodeId node, T comparand) where T : IComparable<T>
        {
            if (comparand.IsLessThan(map[node]))
            {
                map[node] = comparand;
            }
        }

        public static bool IsLessThan<T>(this T value, T other) where T : IComparable<T>
        {
            return value.CompareTo(other) < 0;
        }

        public static bool IsGreaterThan<T>(this T value, T other) where T : IComparable<T>
        {
            return value.CompareTo(other) > 0;
        }

        public static bool Max<T>(this T comparand, ref T value) where T : IComparable<T>
        {
            if (value.CompareTo(comparand) < 0)
            {
                value = comparand;
                return true;
            }

            return false;
        }

        public static bool Min<T>(this T comparand, ref T value) where T : IComparable<T>
        {
            if (value.CompareTo(comparand) > 0)
            {
                value = comparand;
                return true;
            }

            return false;
        }
    }
}
