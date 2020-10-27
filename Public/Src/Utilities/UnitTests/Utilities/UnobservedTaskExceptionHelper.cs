// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// A helper type for testing lack of unobserved task exceptions.
    /// </summary>
    public static class UnobservedTaskExceptionHelper
    {
        /// <nodoc />
        public static async Task RunAsync(Func<Task> func)
        {
            Exception unobservedException = null;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (sender, args) =>
                                                                     {
                                                                         unobservedException = args.Exception;
                                                                     };

            try
            {
                TaskScheduler.UnobservedTaskException += handler;

                await func();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Assert.Null(unobservedException);

            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }
    }
}