// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest;
using Xunit.Abstractions;

namespace BuildXL.Cache.Monitor.Test
{
    public abstract class TestBase : TestWithOutput
    {
        protected TestBase(ITestOutputHelper output) : base(output)
        {
        }

        protected Result<string> GetApplicationKey()
        {
            var applicationKey = Environment.GetEnvironmentVariable("CACHE_MONITOR_APPLICATION_KEY");
            if (string.IsNullOrEmpty(applicationKey))
            {
                return new Result<string>(errorMessage: "Please set the `CACHE_MONITOR_APPLICATION_KEY` environment variable to your application key");
            }
            return applicationKey;
        }
    }
}
