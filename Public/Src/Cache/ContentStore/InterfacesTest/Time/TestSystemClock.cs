// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
