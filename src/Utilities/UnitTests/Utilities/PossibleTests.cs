// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class PossibleTests
    {
        [Fact]
        public void Success()
        {
            Possible<int> success = 1;
            XAssert.IsTrue(success.Succeeded);
            XAssert.AreEqual(1, success.Result);
        }

        [Fact]
        public void Failure()
        {
            Possible<int, Failure<int>> failure = new Failure<int>(2);
            XAssert.IsFalse(failure.Succeeded);
            XAssert.AreEqual(2, failure.Failure.Content);
        }

        [Fact]
        public void RethrowExceptionPreservingStack()
        {
            Possible<int, RecoverableExceptionFailure> maybeInt = FailingFunction();
            XAssert.IsFalse(maybeInt.Succeeded);

            try
            {
                throw maybeInt.Failure.Throw();
            }
            catch (BuildXLException rethrown)
            {
                XAssert.AreEqual("Thrown", rethrown.Message);
                XAssert.IsTrue(rethrown.ToString().Contains("OriginalThrowSite"), "Expected OriginalThrowSite in {0}", rethrown);
            }
        }

        private static Possible<int, RecoverableExceptionFailure> FailingFunction()
        {
            try
            {
                OriginalThrowSite();
                return 1;
            }
            catch (BuildXLException ex)
            {
                return new RecoverableExceptionFailure(ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // We want to XAssert this is in a stack trace.
        private static void OriginalThrowSite()
        {
            throw new BuildXLException("Thrown");
        }

        [Fact]
        public void Bind()
        {
            Possible<int> maybeInt = 1;
            Possible<int> nextMaybeInt = maybeInt.Then(i => new Possible<int>(i + 1));
            XAssert.IsTrue(nextMaybeInt.Succeeded);
            XAssert.AreEqual(2, nextMaybeInt.Result);
        }

        [Fact]
        public void BindOnFailure()
        {
            Possible<int, Failure<int>> maybeInt = new Failure<int>(3);
            Possible<int, Failure<int>> nextMaybeInt = maybeInt.Then(
                i =>
                {
                    XAssert.Fail("Shouldn't be called on failure");
                    return new Possible<int, Failure<int>>(i + 1);
                });

            XAssert.IsFalse(nextMaybeInt.Succeeded);
            XAssert.AreEqual(3, nextMaybeInt.Failure.Content);
        }

        [Fact]
        public void Then()
        {
            Possible<int> maybeInt = 1;
            Possible<int> nextMaybeInt = maybeInt.Then(i => i + 1);
            XAssert.IsTrue(nextMaybeInt.Succeeded);
            XAssert.AreEqual(2, nextMaybeInt.Result);
        }

        [Fact]
        public void ThenOnFailure()
        {
            Possible<int, Failure<int>> maybeInt = new Failure<int>(3);
            Possible<int, Failure<int>> nextMaybeInt = maybeInt.Then(
                i =>
                {
                    XAssert.Fail("Shouldn't be called on failure");
                    return i + 1;
                });

            XAssert.IsFalse(nextMaybeInt.Succeeded);
            XAssert.AreEqual(3, nextMaybeInt.Failure.Content);
        }
    }
}
