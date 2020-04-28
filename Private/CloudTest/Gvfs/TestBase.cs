// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Xunit.Abstractions;

namespace BuildXL.CloudTest.Gvfs
{
    public class TestBase
    {
        private ITestOutputHelper m_testOutput;

        public TestBase(ITestOutputHelper testOutput)
        {
            m_testOutput = testOutput;
        }

        public GvfsJournalHelper Clone(string repo = null)
        {
            return GvfsJournalHelper.Clone(m_testOutput, repo);
        }
    }
}