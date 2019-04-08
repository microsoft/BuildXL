// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Globalization;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Enumeration of the types of errors that
    /// can exist in a cache
    /// </summary>
    public enum CacheErrorType
    {
        /// <summary>
        /// Error related to the session
        /// </summary>
        SessionError,

        /// <summary>
        /// Error related to the CasHash
        /// </summary>
        CasHashError,

        /// <summary>
        /// Error related to the StrongFingerprint
        /// </summary>
        StrongFingerprintError,

        /// <summary>
        /// Error related to Determinism
        /// </summary>
        DeterminismError,
    }

    /// <summary>
    /// Class used to store information for the errors
    /// found in the cache
    /// </summary>
    public class CacheError
    {
        /// <summary>
        /// Type of cache error
        /// </summary>
        public readonly CacheErrorType Type;

        /// <summary>
        /// Describes more about the source of the error
        /// </summary>
        public readonly string Description;

        /// <summary>
        /// Constuctor for a cache error
        /// </summary>
        /// <param name="type">
        /// The type of the CacheError
        /// </param>
        /// <param name="description">
        /// A description of the CacheError, used
        /// for giving more information about the
        /// source of the error
        /// </param>
        public CacheError(CacheErrorType type, string description)
        {
            Contract.Requires(description != null);

            Type = type;
            Description = description;
        }

        private const string CSVFormat = "{0}, {1}";

        /// <summary>
        /// Returns the header for a CSV file containing cache errors
        /// </summary>
        /// <returns>CSV style header</returns>
        public static string GetHeader()
        {
            string returnValue = string.Format(CultureInfo.InvariantCulture, CSVFormat, "Error Type", "Description");
            return returnValue;
        }

        /// <summary>
        /// Returns a string containing all the
        /// information about the cache error
        /// </summary>
        /// <returns>
        /// A string containing all the
        /// information about the cache error
        /// </returns>
        public override string ToString()
        {
            string returnValue = string.Format(CultureInfo.InvariantCulture, CSVFormat, Type.ToString(), Description);
            return returnValue;
        }
    }
}
