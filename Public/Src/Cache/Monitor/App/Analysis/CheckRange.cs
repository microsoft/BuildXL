using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.Monitor.App.Analysis
{
    internal class CheckRange<T>
    {
        private readonly IComparer<T> _comparer;
        private readonly T _minimum;
        private readonly T _maximum;

        public CheckRange(IComparer<T> comparer, T minimum, T maximum)
        {
            Contract.RequiresNotNull(comparer);

            _comparer = comparer;
            _minimum = minimum;
            _maximum = maximum;
        }

        public void Check(IEnumerable<T> elements, Action<int, T> action)
        {
            Contract.RequiresNotNull(elements);
            Contract.RequiresNotNull(action);

            int i = 0;
            foreach (var element in elements)
            {
                if (_comparer.Compare(element, _minimum) < 0)
                {
                    action(i, element);
                }
                else if (_comparer.Compare(element, _maximum) > 0)
                {
                    action(i, element);
                }

                i++;
            }
        }
    }
}
