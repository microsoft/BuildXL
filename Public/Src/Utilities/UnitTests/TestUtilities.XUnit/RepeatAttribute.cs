// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit.Sdk;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Custom attribute to run a test multiple times. Useful for diagnosing flaky tests.
    /// The method to repeat should be a [Theory] with the last argument being the attempt number (an int)
    /// The additional arguments to inject should be specified after the repeat count (see examples below)
    /// </summary>
    /// <remarks>
    /// Examples:
    ///     
    ///     [Repeat(100)]
    ///     [Theory]
    ///     public void MyTestWithAttemptNumber(int attemptNumber) { ... }
    ///     
    ///     [Repeat(100, true)]    // testParam will be true
    ///     [Theory]
    ///     public void MyTestWithParameters(bool testParam, int attemptNumber) { ... }
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RepeatAttribute : DataAttribute
    {
        private readonly int m_count;
        private readonly object[] m_inlineData;
        private readonly int m_parameterCount;

        /// <nodoc />
        public RepeatAttribute(int count, params object[] inlineData)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Repeat count must be greater than 0");
            }

            m_count = count;
            m_parameterCount = inlineData.Length;
            m_inlineData = inlineData;
        }

        /// <nodoc />
        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            for (var i = 0; i < m_count; i++)
            {
                if (m_parameterCount == 0)
                {
                    yield return new object[] { i };
                }
                else
                {
                    object[] data = new object[m_parameterCount + 1];
                    Array.Copy(m_inlineData, data, m_parameterCount);
                    data[m_parameterCount] = i;
                    yield return data;
                }
            }
        }
    }
}
