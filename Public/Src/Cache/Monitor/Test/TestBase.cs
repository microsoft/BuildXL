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

        protected BoolResult LoadApplicationKey()
        {
            var cloudBuildProdApplicationKey = Environment.GetEnvironmentVariable("CACHE_MONITOR_PROD_APPLICATION_KEY");
            if (string.IsNullOrEmpty(cloudBuildProdApplicationKey))
            {
                return new BoolResult($"Please specify a configuration file or set the `CACHE_MONITOR_PROD_APPLICATION_KEY` environment variable to your application key");
            }

            var cloudBuildTestApplicationKey = Environment.GetEnvironmentVariable("CACHE_MONITOR_TEST_APPLICATION_KEY");
            if (string.IsNullOrEmpty(cloudBuildTestApplicationKey))
            {
                return new BoolResult($"Please specify a configuration file or set the `CACHE_MONITOR_TEST_APPLICATION_KEY` environment variable to your application key");
            }

            App.Constants.MicrosoftTenantCredentials.AppKey = cloudBuildProdApplicationKey;
            App.Constants.PMETenantCredentials.AppKey = cloudBuildTestApplicationKey;

            return BoolResult.Success;
        }
    }
}
