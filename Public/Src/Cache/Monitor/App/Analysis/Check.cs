using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Cache.Monitor.App.Analysis
{
    internal static class Check
    {
        public struct Evaluation
        {
            public int Index;
            public bool Perform;

            public Evaluation(int index, bool perform)
            {
                Index = index;
                Perform = perform;
            }
        };

        public static IEnumerable<Evaluation> NotInRange<T>(this IEnumerable<T> elements, T minimum, T maximum, IComparer<T> comparer = null)
        {
            if (comparer == null)
            {
                comparer = Comparer<T>.Default;
            }

            return elements.Select((element, index) => {
                var perform = false;
                if (comparer.Compare(element, minimum) < 0 || comparer.Compare(element, maximum) > 0)
                {
                    perform = true;
                }

                return new Evaluation(index, perform);
            });
        }

        public static IEnumerable<Evaluation> OverPercentualDifference(this IEnumerable<double> elements, double maximumPercentualDifference)
        {
            Contract.Requires(maximumPercentualDifference > 0);

            int index = 0;
            double? last = null;
            foreach (var element in elements)
            {
                if (!(last is null))
                {
                    var percentualDifference = Math.Abs(element - last.Value) / Math.Abs(last.Value);
                    yield return new Evaluation(index, last.Value != 0 && percentualDifference >= maximumPercentualDifference);
                }

                last = element;
                index++;
            }
        }

        public static void Perform(this IEnumerable<Evaluation> evaluations, Action<int> action)
        {
            Contract.RequiresNotNull(evaluations);
            Contract.RequiresNotNull(action);

            foreach (var evaluation in evaluations.Where(e => e.Perform))
            {
                action(evaluation.Index);
            }
        }

        public static void PerformOnLast(this IEnumerable<Evaluation> evaluations, Action<int> action)
        {
            Contract.RequiresNotNull(evaluations);
            Contract.RequiresNotNull(action);

            Evaluation? last = null;
            try
            {
                last = evaluations.Where(e => e.Perform).Last();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (last != null)
            {
                action(last.Value.Index);
            }
        }

        public static IEnumerable<Evaluation> And(this IEnumerable<Evaluation> left, IEnumerable<Evaluation> right)
        {
            Contract.RequiresNotNull(left);
            Contract.RequiresNotNull(right);

            return left.Zip(right, (l, r) => new Evaluation(l.Index, l.Perform && r.Perform));
        }

        public static IEnumerable<Evaluation> Or(this IEnumerable<Evaluation> left, IEnumerable<Evaluation> right)
        {
            Contract.RequiresNotNull(left);
            Contract.RequiresNotNull(right);

            return left.Zip(right, (l, r) => new Evaluation(l.Index, l.Perform || r.Perform));
        }

        public static IEnumerable<Evaluation> Complement(this IEnumerable<Evaluation> evaluations)
        {
            Contract.RequiresNotNull(evaluations);

            return evaluations.Select(evaluation => new Evaluation(evaluation.Index, !evaluation.Perform));
        }
    }
}
