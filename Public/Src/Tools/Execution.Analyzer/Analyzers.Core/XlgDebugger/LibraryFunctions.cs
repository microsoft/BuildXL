// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Execution.Analyzer.JPath;
using static BuildXL.Execution.Analyzer.JPath.Evaluator;

namespace BuildXL.Execution.Analyzer
{
    public static class LibraryFunctions
    {
        public static readonly Function SaveFunction = new Function(name: "save", minArity: 2, func: Save);
        public static readonly Function AppendFunction = new Function(name: "append", minArity: 2, func: Append);

        public static readonly IReadOnlyList<Function> All = new List<Function>
        {
             new Function(name: "sum",    minArity: 1, func: Sum),
             new Function(name: "cut",    minArity: 1, func: Cut),
             new Function(name: "count",  minArity: 1, func: Count),
             new Function(name: "uniq",   minArity: 1, func: Uniq),
             new Function(name: "sort",   minArity: 1, func: Sort),
             new Function(name: "join",   minArity: 1, func: Join),
             new Function(name: "grep",   minArity: 2, func: Grep),
             new Function(name: "strcat", minArity: 1, func: StrCat),
             new Function(name: "head",   minArity: 1, func: Head),
             new Function(name: "tail",   minArity: 1, func: Tail),
             new Function(name: "toJson", minArity: 1, func: ToJson),
             new Function(name: "toCsv",  minArity: 1, func: ToCsv),
             SaveFunction,
             AppendFunction
        };

        private static Result Sum(Evaluator.Args args)
        {
            return args
                .Flatten()
                .Select(obj => args.ToNumber(obj))
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
                ? objs.OrderBy(args.ToNumber)
                : objs.OrderBy(args.Preview);

            var finalOrder = args.HasSwitch("r") // reverse
                ? ordered.Reverse()
                : ordered;

            return finalOrder.ToArray();
        }

        private static Result Join(Evaluator.Args args) => StrCat(args, defaultSeparator: Environment.NewLine);

        private static Result StrCat(Evaluator.Args args) => StrCat(args, defaultSeparator: string.Empty);

        private static Result StrCat(Evaluator.Args args, string defaultSeparator)
        {
            return string.Join(
                args.GetStrSwitch("d", defaultSeparator),
                args.Flatten().Select(args.Preview));
        }

        private static Result Head(Evaluator.Args args)
        {
            var numElements = (int)args.GetNumSwitch("n", 10);
            return args.Flatten().Take(numElements).ToArray();
        }

        private static Result Tail(Evaluator.Args args)
        {
            var numElements = (int)args.GetNumSwitch("n", 10);
            var flattened = args.Flatten().ToList();
            return flattened.Skip(flattened.Count - numElements).ToArray();
        }

        private static Result ToJson(Evaluator.Args args)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(ExtractObjects(args), Newtonsoft.Json.Formatting.Indented);
        }

        private static Result ToCsv(Evaluator.Args args)
        {
            return string.Join(
                Environment.NewLine,
                ExtractObjects(args).Select(dict => string.Join(
                    ",", 
                    dict.Values.Select(v => v?.ToString()).Select(csvEscape))));

            static string csvEscape(string str)
            {
                return '"' + str?.Replace("\"", "\"\"") ?? string.Empty + '"';
            }
        }

        private static Dictionary<string, object>[] ExtractObjects(Evaluator.Args args)
        {
            return args
                .Flatten()
                .Select(o => args.Eval.Resolve(o))
                .Select(o => o.Properties.ToDictionary(p => p.Name, p => extractValue(p.Value)))
                .ToArray();

            static object extractValue(object val)
            {
                return val switch
                {
                    Result r => r.IsScalar ? r.Value.First() : r.Value.ToArray(),
                    _        => val,
                };
            }
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

        private static Result Save(Evaluator.Args args) => SaveToFile(args, append: false);
        private static Result Append(Evaluator.Args args) => SaveToFile(args, append: true);

        private static Result SaveToFile(Evaluator.Args args, bool append)
        {
            var fileName = args.Eval.ToString(args[0]);
            var lines = args
                .Skip(1)
                .SelectMany(result => result)
                .Select(args.Preview)
                .ToList();
            if (append)
            {
                File.AppendAllLines(fileName, lines, System.Text.Encoding.UTF8);
            }
            else
            {
                File.WriteAllLines(fileName, lines, System.Text.Encoding.UTF8);
            }
            return $"Saved {lines.Count} lines to file '{Path.GetFullPath(fileName)}'";
        }
    }
}
