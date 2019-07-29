// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class HelpersTests
    {
        private const int MaxAttempts = 3;
        private const int InitialTimeoutMs = 10;
        private const int PostTimeoutMultiplier = 1;

        [Fact]
        public void RetryOnFailureTest()
        {            
            int attemptCount = 0;
            bool result = Helpers.RetryOnFailure(
                lastAttempt => 
                { 
                    attemptCount++;
                    return true;
                }, 
                numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier);
            
            XAssert.IsTrue(result && attemptCount == 1);

            attemptCount = 0;
            result = Helpers.RetryOnFailure(
                lastAttempt =>
                {
                    attemptCount++;
                    return false;
                }, 
                numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier);
            
            XAssert.IsTrue(!result && attemptCount == MaxAttempts);

            attemptCount = 0;
            var possibleResult = Helpers.RetryOnFailure(
                lastAttempt =>
                { 
                    attemptCount++;
                    return new Possible<string, Failure>(string.Empty);
                }, 
                numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier);

            XAssert.IsTrue(possibleResult.Succeeded && attemptCount == 1);

            attemptCount = 0;
            possibleResult = Helpers.RetryOnFailure(
                lastAttempt =>
                {
                    attemptCount++;
                    return new Possible<string, Failure>(new Failure<string>(string.Empty));
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier);

            XAssert.IsTrue(!possibleResult.Succeeded && attemptCount == MaxAttempts);
        }

        [Fact]
        public void RetryOnFailureAsyncTest()
        {
            int attemptCount = 0;
            var possibleResult = Helpers.RetryOnFailureAsync(
                lastAttempt =>
                {
                    attemptCount++;
                    return Task.FromResult(new Possible<string, Failure>(string.Empty));
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier).GetAwaiter().GetResult();
            
            XAssert.IsTrue(possibleResult.Succeeded && attemptCount == 1);

            attemptCount = 0;
            possibleResult = Helpers.RetryOnFailureAsync(
                lastAttempt =>
                {
                    attemptCount++;
                    return Task.FromResult(new Possible<string, Failure>(new Failure<string>(string.Empty)));
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier).GetAwaiter().GetResult();

            XAssert.IsTrue(!possibleResult.Succeeded && attemptCount == MaxAttempts);
        }

        [Fact]
        public void RetryOnExceptionTest()
        {
            int attemptCount = 0;
            bool result = Helpers.RetryOnFailure(
                lastAttempt =>
                {
                    attemptCount++;
                    throw new BuildXLException(string.Empty);
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier);
            XAssert.IsFalse(result);
            XAssert.IsTrue(attemptCount == MaxAttempts);

            attemptCount = 0;
            Possible<string> possibleResult = Helpers.RetryOnFailure<string>(
                lastAttempt =>
                {
                    attemptCount++;
                    throw new BuildXLException(string.Empty);
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier);

            XAssert.IsTrue(!possibleResult.Succeeded && attemptCount == MaxAttempts);
        }

        [Fact]
        public void RetryOnExceptionButRethrowTest()
        {
            int attemptCount = 0;

            try
            {
                bool result = Helpers.RetryOnFailure(
                    lastAttempt =>
                    {
                        attemptCount++;
                        throw new BuildXLException(string.Empty);
                    },
                    rethrowException: true,
                    numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier);

                XAssert.IsTrue(false, "Should be unreachable");
            }
            catch (BuildXLException)
            {
                XAssert.AreEqual(1, attemptCount);
            }

            try
            {
                attemptCount = 0;
                Possible<string> possibleResult = Helpers.RetryOnFailure<string>(
                    lastAttempt =>
                    {
                        attemptCount++;
                        throw new BuildXLException(string.Empty);
                    },
                    rethrowException: true,
                    numberOfAttempts: MaxAttempts, initialTimeoutMs: InitialTimeoutMs, postTimeoutMultiplier: PostTimeoutMultiplier);

                XAssert.IsTrue(false, "Should be unreachable");
            }
            catch (BuildXLException)
            {
                XAssert.AreEqual(1, attemptCount);
            }
        }
    }
}