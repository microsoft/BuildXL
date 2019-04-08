// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        void Increment();
    }
}
