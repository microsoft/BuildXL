// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using Test.BuildXL.TestUtilities.Xunit;
using Tool.ServicePipDaemon;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.DropDaemon
{
    public class ReloaderTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public ReloaderTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestConcurrentReloadsOfSameVersionCreateOnlyOneNewValue()
        {
            int cnt = 0;
            using (var reloader = new Reloader<string>(() => "hi" + Interlocked.Increment(ref cnt)))
            {
                // call 'Reload(startVersion)' concurrently from multiple threads
                var startVersion = reloader.CurrentVersion;
                var range = Enumerable.Range(0, 100).ToList();
                var threads = range.Select(_ => new Thread(() =>
                {
                    reloader.Reload(startVersion);
                    var instance = reloader.CurrentVersionedValue;

                    // assert each thread gets the same instance
                    XAssert.AreEqual(startVersion + 1, instance.Version);
                    XAssert.AreEqual("hi1", instance.Value);
                })).ToList();
                ConcurrencyTest.Start(threads);
                ConcurrencyTest.Join(threads);

                // assert only 1 new value was created
                XAssert.AreEqual(startVersion + 1, reloader.CurrentVersion);
                XAssert.AreEqual(startVersion + 1, reloader.CurrentVersionedValue.Version);
                XAssert.AreEqual("hi1", reloader.CurrentVersionedValue.Value);
            }
        }

        [Fact]
        public void TestInitialValues()
        {
            int constructorCnt = 0;

            var reloader = new Reloader<int>(constructor: () => Interlocked.Increment(ref constructorCnt));
            XAssert.AreEqual(0, constructorCnt, "Constructor should not be called as soon as Reloader is created");

            XAssert.AreEqual(0, reloader.CurrentVersion);
            XAssert.AreEqual(0, constructorCnt, "Constructor should not be called except in the Reload method");

            XAssert.AreEqual(null, reloader.CurrentVersionedValue);
            XAssert.AreEqual(0, constructorCnt, "Constructor should not be called except in the Reload method");

            reloader.EnsureLoaded();
            XAssert.AreNotEqual(1, reloader.CurrentVersionedValue);
            XAssert.AreEqual(1, constructorCnt, "Constructor should be called in EnsureLoaded method");
        }

        [Fact]
        public void DestructorNotCalledBeforeReloaderIsDisposed()
        {
            int constructorCnt = 0;
            int destructorCnt = 0;

            // create reloader and assert constructor was called
            var reloader = new Reloader<int>(
                constructor: () => Interlocked.Increment(ref constructorCnt),
                destructor: (i) => Interlocked.Increment(ref destructorCnt));
            XAssert.AreEqual(0, constructorCnt, "Constructor should not be called as soon as Reloader is created");
            XAssert.AreEqual(0, destructorCnt, "Destructor should not be called before Reloader is disposed.");

            // reload and assert constructor was called again, but no destructor
            reloader.Reload(reloader.CurrentVersion);
            XAssert.AreEqual(1, constructorCnt);
            XAssert.AreEqual(0, destructorCnt, "Destructor should not be called before Reloader is disposed.");

            // reload and assert constructor was called again, but no destructor
            reloader.Reload(reloader.CurrentVersion);
            XAssert.AreEqual(2, constructorCnt);
            XAssert.AreEqual(0, destructorCnt, "Destructor should not be called before Reloader is disposed.");

            // dispose and assert that destructor was called twice (once for each reloaded value)
            reloader.Dispose();
            XAssert.AreEqual(2, destructorCnt, "Destructor wasn't called for each created value.");
        }

        [Fact]
        public void WhenConstructorFailsVersionIsNotIncremented()
        {
            bool firstConstructorInvocation = true;
            var reloader = new Reloader<int>(
                constructor: () =>
                {
                    if (!firstConstructorInvocation)
                    {
                        throw new NotImplementedException();
                    }

                    firstConstructorInvocation = false;
                    return 42;
                });

            // constructor not called yet --> current version 0
            XAssert.AreEqual(0, reloader.CurrentVersion);

            // constructor called the first time --> doesn't throw --> version incremented
            reloader.Reload(0);
            XAssert.AreEqual(1, reloader.CurrentVersion);

            // constructor called the second time --> throws --> version stays the same
            Assert.Throws<NotImplementedException>(() => reloader.Reload(1));
            XAssert.AreEqual(1, reloader.CurrentVersion);
        }
    }
}
