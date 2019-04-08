// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.BasicFilesystem
{
    /// <summary>
    /// Failure trying to get basic filesystem metadata
    /// </summary>
    public class BasicFilesystemMetadataFailure : CacheBaseFailure
    {
        private readonly string m_cacheId;
        private readonly string m_filename;
        private readonly string m_message;
        private readonly Exception m_rootCause;

        /// <summary>
        /// Failure to create session due to duplicated ID
        /// </summary>
        /// <param name="cacheId">The cache ID</param>
        /// <param name="file">The file that was being accessed during error</param>
        /// <param name="message">The attempted session ID</param>
        /// <param name="rootCause">The root cause exception (optional)</param>
        public BasicFilesystemMetadataFailure(string cacheId, FileStream file, string message, Exception rootCause = null)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(file != null);
            Contract.Requires(message != null);

            m_cacheId = cacheId;
            m_filename = file.Name;
            m_message = message;
            m_rootCause = rootCause;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            if (m_rootCause != null)
            {
                return string.Format(CultureInfo.InvariantCulture, "Metadata error in cache [{0}]: ({1}) : {2}\nRoot Cause: {3}", m_cacheId, m_filename, m_message, m_rootCause);
            }

            return string.Format(CultureInfo.InvariantCulture, "Metadata error in cache [{0}]: ({1}) : {2}", m_cacheId, m_filename, m_message);
        }
    }
}
