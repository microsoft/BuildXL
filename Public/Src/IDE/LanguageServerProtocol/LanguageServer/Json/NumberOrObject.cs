// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace LanguageServer.Json
{
    /// <nodoc />
    public sealed class NumberOrObject<TNumber, TObject> : Either<TNumber, TObject>
        where TNumber : struct, IComparable
    {
        /// <nodoc />
        public static implicit operator NumberOrObject<TNumber, TObject>(TNumber left)
            => new NumberOrObject<TNumber, TObject>(left);

        /// <nodoc />
        public static implicit operator NumberOrObject<TNumber, TObject>(TObject right)
            => new NumberOrObject<TNumber, TObject>(right);

        /// <nodoc />
        public NumberOrObject()
        {
        }

        /// <nodoc />
        public NumberOrObject(TNumber left)
            : base(left)
        {
        }

        /// <nodoc />
        public NumberOrObject(TObject right)
            : base(right)
        {
        }

        /// <nodoc />
        protected override EitherTag OnDeserializing(JsonDataType jsonType)
        {
            return
                (jsonType == JsonDataType.Number) ? EitherTag.Left :
                (jsonType == JsonDataType.Object) ? EitherTag.Right :
                EitherTag.None;
        }
    }
}
