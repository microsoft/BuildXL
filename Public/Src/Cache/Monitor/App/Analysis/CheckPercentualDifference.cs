using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.Monitor.App.Analysis
{
    internal class CheckPercentualDifference<T>
    {
        private readonly Func<T, double> _project;

        private readonly double? _maximumPercentualDifference;

        public CheckPercentualDifference(Func<T, double> project, double maximumPercentualDifference)
        {
            Contract.RequiresNotNull(project);
            Contract.Requires(maximumPercentualDifference > 0);

            _project = project;
            _maximumPercentualDifference = maximumPercentualDifference;
        }

        public void Check(IEnumerable<T> elements, Action<int, T> action)
        {
            Contract.RequiresNotNull(elements);
            Contract.RequiresNotNull(action);

            int i = 0;
            double? lastProjection = null;
            foreach (var element in elements)
            {
                var current = _project(element);
                if (!(lastProjection is null))
                {
                    var last = lastProjection.Value;

                    var percentualDifference = Math.Abs(current - last) / Math.Abs(last);
                    if (last != 0 && percentualDifference >= _maximumPercentualDifference)
                    {
                        action(i, element);
                    }
                }

                lastProjection = current;
                i++;
            }
        }
    }
}
