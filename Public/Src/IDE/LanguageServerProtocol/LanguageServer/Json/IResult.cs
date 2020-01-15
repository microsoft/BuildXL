// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace LanguageServer.Json
{
    /// <summary>
    /// An interface for all the Result types.
    /// Used to simplify ResultConverter.
    /// </summary>
    public interface IResult
    {
        /// <summary>
        /// The object representing an error.
        /// </summary>
        object ErrorObject { get; }

        /// <summary>
        /// The object representing a successful result.
        /// </summary>
        object SuccessObject { get; }
    }
}
