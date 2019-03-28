// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Binding
{
    /// <nodoc />
    [Flags]
    public enum Reachability
    {
        /// <nodoc />
        Unintialized = 1 << 0,

        /// <nodoc />
        Reachable = 1 << 1,

        /// <nodoc />
        Unreachable = 1 << 2,

        /// <nodoc />
        ReportedUnreachable = 1 << 3,
    }

    internal static class ReachabilityExtensions
    {
        /// <nodoc />
        public static Reachability Or(Reachability state1, Reachability state2)
        {
            if (((state1 | state2) & Reachability.Reachable) == Reachability.Reachable)
            {
                return Reachability.Reachable;
            }

            if ((state1 & state2 & Reachability.ReportedUnreachable) == Reachability.ReportedUnreachable)
            {
                return Reachability.ReportedUnreachable;
            }

            return Reachability.Unreachable;
        }
    }
}
