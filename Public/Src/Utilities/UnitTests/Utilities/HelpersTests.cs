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
        [Fact]
        public void RetryOnFailureTest()
        {            
            const int MaxAttempts = 3;

            int attemptCount = 0;
            bool result = Helpers.RetryOnFailure(
                lastAttempt => 
                { 
                    attemptCount++;
                    return true;
                }, 
                numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1);
            
            XAssert.IsTrue(result && attemptCount == 1);

            attemptCount = 0;
            result = Helpers.RetryOnFailure(
                lastAttempt =>
                {
                    attemptCount++;
                    return false;
                }, 
                numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1);
            
            XAssert.IsTrue(!result && attemptCount == MaxAttempts);

            attemptCount = 0;
            var possibleResult = Helpers.RetryOnFailure(
                lastAttempt =>
                { 
                    attemptCount++;
                    return new Possible<string, Failure>(string.Empty);
                }, 
                numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1);

            XAssert.IsTrue(possibleResult.Succeeded && attemptCount == 1);

            attemptCount = 0;
            possibleResult = Helpers.RetryOnFailure(
                lastAttempt =>
                {
                    attemptCount++;
                    return new Possible<string, Failure>(new Failure<string>(string.Empty));
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1);

            XAssert.IsTrue(!possibleResult.Succeeded && attemptCount == MaxAttempts);
        }

        [Fact]
        public void RetryOnFailureAsyncTest()
        {
            const int MaxAttempts = 3;

            int attemptCount = 0;
            var possibleResult = Helpers.RetryOnFailureAsync(
                lastAttempt =>
                {
                    attemptCount++;
                    return Task.FromResult(new Possible<string, Failure>(string.Empty));
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1).GetAwaiter().GetResult();
            
            XAssert.IsTrue(possibleResult.Succeeded && attemptCount == 1);

            attemptCount = 0;
            possibleResult = Helpers.RetryOnFailureAsync(
                lastAttempt =>
                {
                    attemptCount++;
                    return Task.FromResult(new Possible<string, Failure>(new Failure<string>(string.Empty)));
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1).GetAwaiter().GetResult();

            XAssert.IsTrue(!possibleResult.Succeeded && attemptCount == MaxAttempts);
        }

        [Fact]
        public void RetryOnExceptionTest()
        {
            const int MaxAttempts = 3;
            int attemptCount = 0;
            bool result = Helpers.RetryOnFailure(
                lastAttempt =>
                {
                    attemptCount++;
                    throw new BuildXLException(string.Empty);
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1);
            XAssert.IsFalse(result);
            XAssert.IsTrue(attemptCount == MaxAttempts);

            attemptCount = 0;
            Possible<string> possibleResult = Helpers.RetryOnFailure<string>(
                lastAttempt =>
                {
                    attemptCount++;
                    throw new BuildXLException(string.Empty);
                },
                numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1);

            XAssert.IsTrue(!possibleResult.Succeeded && attemptCount == MaxAttempts);
        }

        [Fact]
        public void RetryOnExceptionButRethrowTest()
        {
            const int MaxAttempts = 3;
            int attemptCount = 0;

            try
            {
                bool result = Helpers.RetryOnFailure(
                    lastAttempt =>
                    {
                        attemptCount++;
                        throw new BuildXLException(string.Empty);
                    },
                    onException: e => throw e,
                    numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1);

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
                    onException: e => throw e,
                    numberOfAttempts: MaxAttempts, initialTimeoutMs: 100, postTimeoutMultiplier: 1);

                XAssert.IsTrue(false, "Should be unreachable");
            }
            catch (BuildXLException)
            {
                XAssert.AreEqual(1, attemptCount);
            }
        }
    }
}