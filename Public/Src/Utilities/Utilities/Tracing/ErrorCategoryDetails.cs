// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Details about errors logged for a particular error category
    /// </summary>
    public class ErrorCagetoryDetails
    {
        private int m_numErrors;

        /// <summary>
        /// The name of the first error of this category logged
        /// </summary>
        public string FirstErrorName { get; private set; }

        /// <summary>
        /// The message for the first error logged of this category
        /// </summary>
        public string FirstErrorMessage { get; private set; }

        /// <summary>
        /// THe name of the last error of this category logged
        /// </summary>
        public string LastErrorName { get; private set; }

        /// <summary>
        /// The message for the last error logged of this category
        /// </summary>
        public string LastErrorMessage { get; private set; }

        /// <summary>
        /// The number of errors logged for the category
        /// </summary>
        public int Count => Volatile.Read(ref m_numErrors);

        /// <summary>
        /// Registers an error for this category
        /// </summary>
        /// <param name="errorName">Name of the error bucket</param>
        /// <param name="errorMessage">Message about the details of this error</param>
        public void RegisterError(string errorName, string errorMessage)
        {
            if ((Interlocked.Increment(ref m_numErrors) == 1))
            {
                FirstErrorName = errorName;
                FirstErrorMessage = errorMessage;
            }
            else
            {
                LastErrorName = errorName;
                LastErrorMessage = errorMessage;
            }
        }
    }
}
