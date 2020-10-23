// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Time
{
    /// <summary>
    ///     Mockable, stepping clock interface to aid in unit testing
    /// </summary>
    public interface ITestClock : IClock
    {
        /// <summary>
        ///     Make sure clock is incremented by some amount.
        /// </summary>
        DateTime Increment();
    }
}
