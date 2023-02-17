// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Used to identify existing file deletion failures
    /// </summary>
    public class FailToDeleteForMaterializationFailure : Failure
    {
        private const string ExistingFileDeletionFailure = "Try materialize file from cache failed due to file deletion failure";

        /// <nodoc/>
        public FailToDeleteForMaterializationFailure(Failure innerFailure = null)
            : base(innerFailure) { }

        /// <inheritdoc/>
        public override BuildXLException CreateException()
        {
            return new BuildXLException(ExistingFileDeletionFailure);
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return ExistingFileDeletionFailure;
        }

        /// <inheritdoc/>
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}
