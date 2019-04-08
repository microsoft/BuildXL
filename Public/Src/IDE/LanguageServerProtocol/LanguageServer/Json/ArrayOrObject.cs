// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LanguageServer.Json
{
    /// <nodoc />
    public sealed class ArrayOrObject<TElement, TObject> : Either<TElement[], TObject>
    {
        /// <nodoc />
        public static implicit operator ArrayOrObject<TElement, TObject>(TElement[] left)
            => new ArrayOrObject<TElement, TObject>(left);

        /// <nodoc />
        public static implicit operator ArrayOrObject<TElement, TObject>(TObject right)
            => new ArrayOrObject<TElement, TObject>(right);

        /// <nodoc />
        public ArrayOrObject()
        {
        }

        /// <nodoc />
        public ArrayOrObject(TElement[] left)
            : base(left)
        {
        }

        /// <nodoc />
        public ArrayOrObject(TObject right)
            : base(right)
        {
        }

        /// <nodoc />
        protected override EitherTag OnDeserializing(JsonDataType jsonType)
        {
            return
                (jsonType == JsonDataType.Array) ? EitherTag.Left :
                (jsonType == JsonDataType.Object) ? EitherTag.Right :
                EitherTag.None;
        }
    }
}
