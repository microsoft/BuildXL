// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public sealed class TaskUtilitiesTests
    {
        [Fact]
        public async Task FromException()
        {
            var toThrow = new BuildXLException("Heave, ho");
            try
            {
                await TaskUtilities.FromException<int>(toThrow);
            }
            catch (BuildXLException ex)
            {
                XAssert.AreSame(toThrow, ex);
                return;
            }

            XAssert.Fail("Expected an exception");
        }

        private int ThrowNull()
        {
            throw new NullReferenceException();
        }

        private static int ThrowDivideByZero()
        {
            throw new DivideByZeroException();
        }

        [Fact]
        public async Task SafeWhenAll()
        {
            try
            {
                await TaskUtilities.SafeWhenAll(
                    new[]
                    {
                        (Task)Task.Run(() => ThrowNull()),
                        (Task)Task.Run(() => ThrowDivideByZero())
                    });
            }
            catch (AggregateException aggregateException)
            {
                XAssert.IsNotNull(aggregateException.InnerExceptions.OfType<NullReferenceException>().FirstOrDefault());
                XAssert.IsNotNull(aggregateException.InnerExceptions.OfType<DivideByZeroException>().FirstOrDefault());
            }
        }

        [Fact]
        public async Task SafeWhenAllGeneric()
        {
            try
            {
                await TaskUtilities.SafeWhenAll<int>(
                    new[]
                    {
                        Task.Run(() => ThrowNull()),
                        Task.Run(() => ThrowDivideByZero())
                    });
            }
            catch (AggregateException aggregateException)
            {
                XAssert.IsNotNull(aggregateException.InnerExceptions.OfType<NullReferenceException>().FirstOrDefault());
                XAssert.IsNotNull(aggregateException.InnerExceptions.OfType<DivideByZeroException>().FirstOrDefault());
            }
        }
    }
}
