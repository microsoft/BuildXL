// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
