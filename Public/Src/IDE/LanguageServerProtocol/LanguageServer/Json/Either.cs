// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using LanguageServer.Infrastructure.JsonDotNet;
using Newtonsoft.Json;
using System;

namespace LanguageServer.Json
{
    /// <nodoc />
    [JsonConverter(typeof(EitherConverter))]
    public abstract class Either<TLeft, TRight> : IEither
    {
        private EitherTag _tag;
        private TLeft _left;
        private TRight _right;

        /// <nodoc />
        public Either()
        {
        }

        /// <nodoc />
        public Either(TLeft left)
        {
            _tag = EitherTag.Left;
            _left = left;
        }

        /// <nodoc />
        public Either(TRight right)
        {
            _tag = EitherTag.Right;
            _right = right;
        }

        /// <nodoc />
        public bool IsLeft => _tag == EitherTag.Left;

        /// <nodoc />
        public bool IsRight => _tag == EitherTag.Right;

        /// <nodoc />
        public TLeft Left
        {
            get
            {
                if (_tag == EitherTag.Left)
                {
                    return _left;
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
        }

        /// <nodoc />
        public TRight Right
        {
            get
            {
                if (_tag == EitherTag.Right)
                {
                    return _right;
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
        }

        /// <nodoc />
        public Type LeftType => typeof(TLeft);

        /// <nodoc />
        public Type RightType => typeof(TRight);

        /// <nodoc />
        protected abstract EitherTag OnDeserializing(JsonDataType jsonType);

        /// <nodoc />
        object IEither.Left
        {
            get { return this.Left; }

            set
            {
                _tag = EitherTag.Left;
                _left = (TLeft)value;
                _right = default(TRight);
            }
        }

        /// <nodoc />
        object IEither.Right
        {
            get { return this.Right; }

            set
            {
                _tag = EitherTag.Right;
                _left = default(TLeft);
                _right = (TRight)value;
            }
        }

        /// <nodoc />
        EitherTag IEither.OnDeserializing(JsonDataType jsonType) => this.OnDeserializing(jsonType);
    }
}
