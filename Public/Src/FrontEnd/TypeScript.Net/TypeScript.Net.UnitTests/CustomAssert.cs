// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TypeScript.Net.UnitTests
{
    internal static class CustomAssert
    {
        public static void True(bool predicate, string message)
        {
            if (!predicate)
            {
                throw new Xunit.Sdk.XunitException($"Assertion failed: {message}");
            }
        }

        public static void Fail(string message)
        {
            throw new Xunit.Sdk.XunitException(message);
        }
    }
}
