// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AdoBuildRunner;
using BuildXL.AdoBuildRunner.Vsts;

namespace Test.Tool.AdoBuildRunner
{
    public class AdoBuildRunnerTestBase
    {
        /// <nodoc />
        protected MockAdoAPIService MockAdoApiService { get; private set; }

        /// <nodoc />
        protected MockAdoEnvironment MockAdoEnvironment { get;  set; }

        /// <nodoc />
        protected MockAdoBuildRunnerConfig MockConfig { get; set; }

        /// <nodoc />
        protected MockLogger MockLogger { get; private set; }

        /// <nodoc />
        public AdoBuildRunnerTestBase()
        {
            MockAdoApiService = new MockAdoAPIService();
            MockLogger = new MockLogger();
            MockConfig = new MockAdoBuildRunnerConfig();
            MockAdoEnvironment = new MockAdoEnvironment();
        }

        /// <summary>
        /// Instantiate AdoBuildRunnerService for testing purpose
        /// </summary>
        public AdoBuildRunnerService CreateAdoBuildRunnerService()
        {
            return new AdoBuildRunnerService(MockLogger, MockAdoEnvironment, MockAdoApiService, MockConfig);
        }
    }
}
