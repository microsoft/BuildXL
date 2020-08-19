// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Used to identify timeout failure for materialization or pin
    /// </summary>
    public class TimeoutForArtifactContentCacheFailure : Failure
    {
        private readonly string m_failureMessage;

        /// <nodoc/>
        public TimeoutForArtifactContentCacheFailure(string operation, int timeoutDurationMin, Failure innerFailure = null)
            : base(innerFailure) 
        {
            m_failureMessage = $"{operation} failed due to timeout: {timeoutDurationMin} minutes";
        }

        /// <inheritdoc/>
        public override BuildXLException CreateException()
        {
            return new BuildXLException(m_failureMessage);
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return m_failureMessage;
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
