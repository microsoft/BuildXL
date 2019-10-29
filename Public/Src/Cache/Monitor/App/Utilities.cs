using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Cache.Monitor.App
{
    internal static class Utilities
    {
        public static void SplitBy<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate, ICollection<T> trueSet, ICollection<T> falseSet)
        {
            // TODO(jubayard): this function can be split in two cases, find the first index at which the predicate is
            // true, and find all entries for which the predicate is true. Need to evaluate case-by-case.
            Contract.RequiresNotNull(enumerable);
            Contract.RequiresNotNull(predicate);
            Contract.RequiresNotNull(trueSet);
            Contract.RequiresNotNull(falseSet);

            foreach (var entry in enumerable)
            {
                if (predicate(entry))
                {
                    trueSet.Add(entry);
                }
                else
                {
                    falseSet.Add(entry);
                }
            }
        }
    }
}
