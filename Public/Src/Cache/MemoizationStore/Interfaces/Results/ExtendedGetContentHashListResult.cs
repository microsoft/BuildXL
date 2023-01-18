// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Results
{
    /// <summary>
    /// Extends a GetContentHashListResult with additional cache aggregation
    /// results.
    /// </summary>
    public class ExtendedGetContentHashListResult : GetContentHashListResult
    {
        /// <summary>
        /// Constructor to match PlaceFileResult (see CloudStore repo).
        /// Applies to following constructors as well.
        /// </summary>
        /// <param name="contentHashListWithDeterminism"></param>
        /// <param name="artifactSource"></param>
        public ExtendedGetContentHashListResult(
            ContentHashListWithDeterminism contentHashListWithDeterminism,
            ArtifactSource artifactSource)
            : base(contentHashListWithDeterminism)
        {
            ArtifactSource = artifactSource;
        }

        /// <nodoc />
        public ExtendedGetContentHashListResult(ResultBase other, string message = null)
            : base(other, message)
        {
        }

        /// <nodoc />
        public ExtendedGetContentHashListResult(
            string errorMessage,
            string diagnostics,
            ArtifactSource artifactSource)
            : base(errorMessage, diagnostics)
        {
            ArtifactSource = artifactSource;
        }

        /// <nodoc />
        public ExtendedGetContentHashListResult(
            ResultBase other,
            string message,
            ArtifactSource artifactSource)
            : base(other, message)
        {
            ArtifactSource = artifactSource;
        }

        /// <summary>
        /// What level of the overall cache the file came from.
        /// </summary>
        public ArtifactSource ArtifactSource { get; }

        /// <summary>
        /// Combines the artifact source and GetCHL result for aggregation.
        /// </summary>
        public static ExtendedGetContentHashListResult FromGetContentHashListResult(
            GetContentHashListResult result,
            ArtifactSource artifactSource)
        {
            if (result.ErrorMessage != null)
            {
                return new ExtendedGetContentHashListResult(result.ErrorMessage, result.Diagnostics, artifactSource);
            }

            return new ExtendedGetContentHashListResult(result.ContentHashListWithDeterminism, artifactSource);
        }
    }
}
