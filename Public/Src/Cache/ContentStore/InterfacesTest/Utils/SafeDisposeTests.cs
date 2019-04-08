// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Utils
{
    public class SafeDisposeTests
    {
        [Fact]
        public void SetsReferenceToNull()
        {
            IDisposable myDisposable = DisposableFactory.CreateDisposable();

            SafeDispose.DisposeAndSetNull(ref myDisposable);

            Assert.Equal(null, myDisposable);
        }

        [Fact]
        public void DisposeCalled()
        {
            bool disposeCalled = false;
            IDisposable myDisposable = DisposableFactory.CreateDisposable(
                () => disposeCalled = true);

            SafeDispose.DisposeAndSetNull(ref myDisposable);

            Assert.True(disposeCalled);
        }

        [Fact]
        public void MultipleCallsSameReference()
        {
            int disposeCount = 0;
            IDisposable myDisposable = DisposableFactory.CreateDisposable(
                () => disposeCount++);

            SafeDispose.DisposeAndSetNull(ref myDisposable);

            // Here it should be null, so nothing will happen, should not throw either
            SafeDispose.DisposeAndSetNull(ref myDisposable);

            Assert.Equal(1, disposeCount);
        }

        [Fact]
        public void NullDisposeDoesNotThrow()
        {
            IDisposable myDisposable = null;
            SafeDispose.DisposeAndSetNull(ref myDisposable);
        }

        [Fact]
        public void ThreadSafeMultipleDispose()
        {
            int disposalBegunCount = 0;
            int disposalCompleteCount = 0;
            var releaseDispose = new AutoResetEvent(false);

            IDisposable myDisposable = DisposableFactory.CreateDisposable(
                () =>
                {
                    disposalBegunCount++;
                    releaseDispose.WaitOne();
                    disposalCompleteCount++;
                });

            Assert.Equal(0, disposalBegunCount);
            Assert.Equal(0, disposalCompleteCount);

            var dispose1Task = Task.Run(
                () => SafeDispose.DisposeAndSetNull(ref myDisposable));
            var dispose2Task = Task.Run(
                () => SafeDispose.DisposeAndSetNull(ref myDisposable));

            // Give them some time to start up
            Thread.Sleep(TimeSpan.FromSeconds(0.5));

            Assert.Equal(1, disposalBegunCount);
            Assert.Equal(0, disposalCompleteCount);

            releaseDispose.Set();

            // Give the tasks time to finish disposal
            dispose1Task.Wait();
            dispose2Task.Wait();

            Assert.Equal(1, disposalBegunCount);
            Assert.Equal(1, disposalCompleteCount);

            // Extra releases should do nothing
            releaseDispose.Set();
            releaseDispose.Set();
            releaseDispose.Set();

            Assert.Equal(1, disposalBegunCount);
            Assert.Equal(1, disposalCompleteCount);
        }

        private static class DisposableFactory
        {
            public static IDisposable CreateDisposable(Action onDispose = null)
            {
                onDispose = onDispose ?? (() => { });
                return new InternalDisposable(onDispose);
            }

            private sealed class InternalDisposable : IDisposable
            {
                private readonly Action _duringDispose;

                public InternalDisposable(Action duringDispose)
                {
                    _duringDispose = duringDispose;
                }

                public void Dispose()
                {
                    _duringDispose();
                }
            }
        }
    }
}
