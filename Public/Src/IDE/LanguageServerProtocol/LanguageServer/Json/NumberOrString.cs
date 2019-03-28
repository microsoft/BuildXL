// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace LanguageServer.Json
{
    /// <nodoc />
    public sealed class NumberOrString : Either<long, string>, IEquatable<NumberOrString>
    {
        /// <nodoc />
        public static implicit operator NumberOrString(long left) => new NumberOrString(left);

        /// <nodoc />
        public static implicit operator NumberOrString(string right) => new NumberOrString(right);

        /// <nodoc />
        public NumberOrString()
        {
        }

        /// <nodoc />
        public NumberOrString(long left)
            : base(left)
        {
        }

        /// <nodoc />
        public NumberOrString(string right)
            : base(right)
        {
        }

        /// <nodoc />
        protected override EitherTag OnDeserializing(JsonDataType jsonType)
        {
            return
                (jsonType == JsonDataType.Number) ? EitherTag.Left :
                (jsonType == JsonDataType.String) ? EitherTag.Right :
                EitherTag.None;
        }

        /// <nodoc />
        public override int GetHashCode() =>
            IsLeft ? Left.GetHashCode() :
            IsRight ? Right.GetHashCode() :
            0;

        /// <nodoc />
        public override bool Equals(object obj)
        {
            var other = obj as NumberOrString;
            return (other == null) ? false : Equals(other);
        }

        /// <nodoc />
        public bool Equals(NumberOrString other) =>
            (IsLeft && other.IsLeft) ? (Left == other.Left) :
            (IsRight && other.IsRight) ? (Right == other.Right) :
            (!IsLeft && !IsRight && !other.IsLeft && !other.IsRight); // None == None
    }
}
