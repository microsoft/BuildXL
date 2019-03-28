// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Represents the find reference notification progress.
    /// </summary>
    /// <remarks>
    /// This must be kept in sync with the VSCode and VS extensions.
    /// {vscode extension location} Public\Src\FrontEnd\IDE\VsCode\client\src\notifications\findReferenceNotification.ts
    /// </remarks>
    [DataContract]
    public sealed class FindReferenceProgressParams
    {
        /// <summary>
        /// Total number of references found in so far.
        /// </summary>
        [DataMember(Name = "numberOfReferences")]
        public int NumberOfReferences { get; set; }

        /// <summary>
        /// Time spent for the find references operation so far.
        /// </summary>
        [DataMember(Name = "pendingDurationInMs")]
        public int ElapsedDurationInMs { get; set; }

        /// <summary>
        /// Number of processed specs.
        /// </summary>
        [DataMember(Name = "numberOfProcessedSpecs")]
        public int NumberOfProcessedSpecs { get; set; }

        /// <summary>
        /// The total number of specs that will be processed.
        /// </summary>
        [DataMember(Name = "totalNumberOfSpecs")]
        public int TotalNumberOfSpecs { get; set; }

        /// <nodoc />
        public static FindReferenceProgressParams Create(int numberOfRerences, int elapsedDurationInMs, int numberOfProcessedSpecs, int totalNumberOfSpecs)
        {
            return new FindReferenceProgressParams
            {
                NumberOfReferences = numberOfRerences,
                ElapsedDurationInMs = elapsedDurationInMs,
                NumberOfProcessedSpecs = numberOfProcessedSpecs,
                TotalNumberOfSpecs = totalNumberOfSpecs
            };
        }

        /// <nodoc />
        public static FindReferenceProgressParams Cancelled(int pendingDurationInMs) => Create(0, pendingDurationInMs, 0, 0);
    }
}
