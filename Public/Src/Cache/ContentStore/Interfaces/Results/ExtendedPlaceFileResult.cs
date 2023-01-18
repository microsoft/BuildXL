// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Extends a PlaceFileResult with additional cache aggregation
    /// results.
    /// </summary>
    public class ExtendedPlaceFileResult : PlaceFileResult
    {
        /// <summary>
        /// Constructor to match PlaceFileResult (see CloudStore repo).
        /// Applies to following constructors as well.
        /// </summary>
        /// <param name="code"></param>
        /// <param name="fileSize"></param>
        /// <param name="artifactSource"></param>
        public ExtendedPlaceFileResult(ResultCode code, long fileSize, ArtifactSource artifactSource)
            : base(code, fileSize) => ArtifactSource = artifactSource;

        /// <nodoc />
        public ExtendedPlaceFileResult(
            ResultCode code,
            string errorMessage,
            ArtifactSource artifactSource,
            string? diagnostics = null)
            : base(code, errorMessage, diagnostics) => ArtifactSource = artifactSource;

        /// <nodoc />
        public ExtendedPlaceFileResult(ResultBase result, string? message = null)
            : base(result, message)
        {
        }

        /// <summary>
        /// What level of the overall cache the file came from.
        /// </summary>
        public ArtifactSource ArtifactSource { get; }

        /// <summary>
        /// Combines the artifact source and PlaceFile result for aggregation.
        /// </summary>
        /// <param name="result"></param>
        /// <param name="artifactSource"></param>
        /// <returns></returns>
        public static ExtendedPlaceFileResult FromPlaceFileResult(PlaceFileResult result, ArtifactSource artifactSource)
        {
            // Explicitly checking for ErrorMessage, but not for !result.Success, because
            // the operation may fail without the error message and the constructor that takes result code
            // expects the message not to be null.
            if (result.ErrorMessage != null)
            {
                return new ExtendedPlaceFileResult(result.Code, result.ErrorMessage, artifactSource, result.Diagnostics);
            }

            return new ExtendedPlaceFileResult(result.Code, result.FileSize, artifactSource);
        }
    }
}
