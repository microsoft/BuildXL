// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace Test.BuildXL
{
    /// <nodoc/>
    public class ServerEnvironmentVariableTests : IDisposable
    {
        private readonly Dictionary<string, string> m_environment = new Dictionary<string, string>();

        public ServerEnvironmentVariableTests()
        {
            foreach (DictionaryEntry kvp in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
            {
                m_environment.Add(kvp.Key.ToString(), kvp.Value.ToString());
            }
        }

        [Fact]
        public void ResetServerEnvironmentVariables()
        {
            const string OldVariableName = "OLDVARIABLE";
            const string ModifiedVariableName = "MODIFIEDVARIABLE";
            const string AddedVariableName = "ADDEDVARIABLE";
            Environment.SetEnvironmentVariable(OldVariableName, "old", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(ModifiedVariableName, "1", EnvironmentVariableTarget.Process);

            List<KeyValuePair<string, string>> targetEnvironment = new List<KeyValuePair<string, string>>();
            targetEnvironment.Add(new KeyValuePair<string, string>(ModifiedVariableName, "2"));
            targetEnvironment.Add(new KeyValuePair<string, string>(AddedVariableName, "new"));

            global::BuildXL.AppServer.SetEnvironmentVariables(targetEnvironment);

            Assert.Null(Environment.GetEnvironmentVariable(OldVariableName, EnvironmentVariableTarget.Process));
            Assert.Equal("2", Environment.GetEnvironmentVariable(ModifiedVariableName));
            Assert.Equal("new", Environment.GetEnvironmentVariable(AddedVariableName));
        }

        public void Dispose()
        {
            // Reset the environment to its previous state to prevent this test from poisioning other tests
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
            {
                Environment.SetEnvironmentVariable(variable.ToString(), null, EnvironmentVariableTarget.Process);
            }

            foreach (var variable in m_environment)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value, EnvironmentVariableTarget.Process);
            }
        }
    }
}
