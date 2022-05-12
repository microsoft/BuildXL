// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.VstsAuthentication
{
    /// <summary>
    /// An authentication related failure when dealing with authenticated NuGet feeds
    /// </summary>
    public class AuthenticationFailure : Failure
    {
        private readonly string m_failure;

        /// <nodoc/>
        public AuthenticationFailure(string failure)
        {
            m_failure = failure;
        }

        /// <inheritdoc/>
        public override BuildXLException CreateException()
        {
            return new BuildXLException(m_failure);
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return m_failure;
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
