// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace LanguageServer.Json
{
    /// <nodoc />
    public interface IEither
    {
        /// <nodoc />
        bool IsLeft { get; }

        /// <nodoc />
        bool IsRight { get; }

        /// <nodoc />
        object Left { get; set; }

        /// <nodoc />
        object Right { get; set; }

        /// <nodoc />
        Type LeftType { get; }

        /// <nodoc />
        Type RightType { get; }

        /// <nodoc />
        EitherTag OnDeserializing(JsonDataType jsonType);
    }
}
