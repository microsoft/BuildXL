// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.Time;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Time
{
    public class TestSystemClock : SystemClock, ITestClock
    {
        /// <summary>
        ///     Singleton instance for all to use
        /// </summary>
        // ReSharper disable once ArrangeModifiersOrder
        public static new readonly TestSystemClock Instance = new TestSystemClock();

        public void Increment()
        {
            Thread.Sleep(1);
        }
    }
}
