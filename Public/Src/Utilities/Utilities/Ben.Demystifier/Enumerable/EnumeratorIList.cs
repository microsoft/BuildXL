// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace System.Collections.Generic.Enumerable
{
    /// <nodoc />
    public struct EnumeratorIList<T> : IEnumerator<T>
    {
        private readonly IList<T> _list;
        private int _index;

        /// <nodoc />
        public EnumeratorIList(IList<T> list)
        {
            _index = -1;
            _list = list;
        }

        /// <nodoc />
        public T Current => _list[_index];
        
        /// <nodoc />
        public bool MoveNext()
        {
            _index++;

            return _index < (_list?.Count ?? 0);
        }

        /// <nodoc />
        public void Dispose() { }

        object IEnumerator.Current => Current;

        /// <nodoc />
        public void Reset() => _index = -1;
    }
}
