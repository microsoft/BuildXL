// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Extensions for string class.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Returns a canonicalized path as a string.
        /// </summary>
        public static string ToCanonicalizedPath(this string pathAsString) => OperatingSystemHelper.CanonicalizePath(pathAsString);

        /// <summary>
        /// Returns a canonicalized environment variable as a string.
        /// </summary>
        public static string ToCanonicalizedEnvVar(this string envVarAsString) => OperatingSystemHelper.CanonicalizeEnvVar(envVarAsString);
    }
}
