// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Execution.Analyzer.JPath;
using static BuildXL.Execution.Analyzer.JPath.Evaluator;

namespace BuildXL.Execution.Analyzer
{
    public static class LibraryFunctions
    {
        public static readonly IReadOnlyList<Function> All = new List<Function>
        {
             new Function(name: "sum",   minArity: 1, func: Sum),
             new Function(name: "cut",   minArity: 1, func: Cut),
             new Function(name: "count", minArity: 1, func: Count),
             new Function(name: "uniq",  minArity: 1, func: Uniq),
             new Function(name: "sort",  minArity: 1, func: Sort),
             new Function(name: "join",  minArity: 1, func: Join),
             new Function(name: "grep",  minArity: 2, func: Grep),
        };

        private static Result Sum(Evaluator.Args args)
        {
            return args
                .Flatten()
                .Select(obj => args.ToInt(obj))
                .Sum();
        }

        private static Result Cut(Evaluator.Args args)
        {
            var separator = args.ToString(args.GetSwitch("d") ?? " \t\r\n");
            var fieldsValue = args.GetSwitch("f");
            var fields = fieldsValue != null
                ? args.Preview(args.ToScalar(fieldsValue))
                : "1";
            var fieldIndices = new HashSet<int>(fields
                .Split(',')
                .SelectMany(f => int.TryParse(f, out var idx) ? new[] { idx - 1 } : new int[0]));
            return args
                .Flatten()
                .Select(obj => string
                    .Join(
                        separator,
                        args
                            .Preview(obj)
                            .Split(separator.ToCharArray(), StringSplitOptions.RemoveEmptyEntries)
                            .Where((_, idx) => fieldIndices.Contains(idx)))
                    .Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }

        private static Result Count(Evaluator.Args args)
        {
            return args.Flatten().Count();
        }

        private static Result Uniq(Evaluator.Args args)
        {
            var groups = args.Flatten().GroupBy(obj => args.Preview(obj));

            if (args.HasSwitch("c")) // count objects in each group
            {
                return groups
                    .Select(grp => $"{grp.Count()}\t{args.Preview(grp.First())}")
                    .ToArray();
            }
            else
            {
                return groups
                    .Select(grp => grp.First())
                    .ToArray();
            }
        }

        private static Result Sort(Evaluator.Args args)
        {
            var objs = args.Flatten();

            var ordered = args.HasSwitch("n") // numeric sorting (otherwise string sorting)
                ? objs.OrderBy(args.ToInt)
                : objs.OrderBy(args.Preview);

            var finalOrder = args.HasSwitch("r") // reverse
                ? ordered.Reverse()
                : ordered;

            return finalOrder.ToArray();
        }

        private static Result Join(Evaluator.Args args)
        {
            var separator = args.GetSwitch("d");
            return string.Join(
                separator != null ? args.ToString(separator) : Environment.NewLine,
                args.Flatten().Select(args.Preview));
        }

        private static Result Grep(Evaluator.Args args)
        {
            var pattern = args[0];
            var flip = args.HasSwitch("v");
            return args
                .Skip(1)
                .SelectMany(result => result)
                .Where(obj => flip ^ args.Matches(args.Preview(obj), pattern))
                .ToArray();
        }
    }
}
