// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A general, recoverable exception thrown by BuildXL. Callers can safely try - catch for BuildXLException
    /// without also catching exceptions that indicate a programming error.
    /// </summary>
    public class BuildXLEnvironments
    {
        /// <summary>
        /// Get a boolean value of a environment variable
        /// </summary>
        public static bool GetFlag(string environmentVariableName)
        {
            var strValue = Environment.GetEnvironmentVariable(environmentVariableName);
            if (string.IsNullOrWhiteSpace(strValue))
            {
                return false;
            }

            switch (strValue.ToLowerInvariant())
            {
                case "1":
                case "true":
                    return true;
                case "0":
                case "false":
                default:
                    return false;
            }
        }
    }
}
