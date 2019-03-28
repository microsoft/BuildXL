// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;

namespace BuildXL.Utilities.CLI
{
    /// <summary>
    ///     A configuration object contains supported configuration options (see <see cref="AllConfigurationOptions"/>,
    ///     provided (configured) option values (see <see cref="ConfiguredOptionValues"/>), and a reference to a
    ///     <see cref="Parser"/> used to parse the options.
    /// </summary>
    /// <remarks>
    ///     Strongly immutable.
    /// </remarks>
    public sealed class Config
    {
        /// <summary>Default parser used by <see cref="ParseCommandLineArgs"/>.</summary>
        public static readonly IParser DefaultParser = new WinParser();

        /// <summary>Parser used to create this configuration.</summary>
        public IParser Parser { get; }

        /// <summary>All supported configuration options.</summary>
        public IReadOnlyCollection<Option> AllConfigurationOptions { get; }

        /// <summary>All provided configuration option values.</summary>
        public ILookup<Option, string> ConfiguredOptionValues { get; }

        /// <summary>
        ///     Takes a collection of supported <see cref="Option"/>s, a queue of strings, and a <see cref="Parser"/>;
        ///     parses all strings from <paramref name="args"/>, ensures that each matches a supported option from
        ///     <paramref name="confOptions"/>, produces a <see cref="Config"/> containing parsed option values,
        ///     then calls <see cref="Validate"/> to ensure that provided options' constraints (e.g.,
        ///     <see cref="Option.IsRequired"/>, <see cref="Option.IsMultiValue"/>, <see cref="Option.TryConvertValue(string)"/>)
        ///     are all met.
        /// </summary>
        /// <remarks>
        ///     <b>Mutates <paramref name="args"/></b> (by virtue of calling <see cref="IParser.Parse(Queue{string})"/>).
        /// </remarks>
        public static Config ParseCommandLineArgs(IEnumerable<Option> confOptions, Queue<string> args, IParser parser = null, bool caseInsensitive = false, bool ignoreInvalidOptions = false)
        {
            Contract.Requires(confOptions != null);
            Contract.Requires(args != null);
            Contract.Requires(args.All(a => !string.IsNullOrWhiteSpace(a)));

            parser = parser ?? DefaultParser;

            var stringComparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

            // create 'indexes' for configured options for quick lookups
            Dictionary<string, Option> longNameIndex;
            Dictionary<string, Option> shortNameIndex;
            CreateIndexes(confOptions, stringComparer, out longNameIndex, out shortNameIndex);

            // parse options and populate 'matched options' dictionary
            var matchedOptions = ParseOptions(args, parser, longNameIndex, shortNameIndex, ignoreInvalidOptions);

            // create, validate, and return new configuration
            var ans = new Config(parser, new List<Option>(confOptions), matchedOptions);
            ans.Validate();
            return ans;
        }

        /// <summary>
        ///     Checks for the following:
        ///         1. all required options are specified;
        ///         2. all options specified multiple times allow for multiple values (<seealso cref="Option.IsMultiValue"/>);
        ///         3. all specified option values are valid (<seealso cref="Option.TryConvertValue(string)"/>).
        ///
        ///     Throws if any of the checks fails.
        /// </summary>
        public void Validate()
        {
            // check if all required options have values
            var missingRequiredOptions = AllConfigurationOptions.Where(opt => opt.IsRequired && !ConfiguredOptionValues.Contains(opt)).ToList();
            Check(missingRequiredOptions.Count == 0, "the following required options are missing: {0}", ToStr(missingRequiredOptions));

            // check for disallowed multi values
            var illegalMultiValueOptions = ConfiguredOptionValues.Where(grp => !grp.Key.IsMultiValue && grp.Count() > 1).ToList();
            Check(illegalMultiValueOptions.Count == 0, "the following options are not allowed to be specified multiple times: {0}", ToStr(illegalMultiValueOptions));

            // check if all set options have valid values
            var invalidOptionValues = ConfiguredOptionValues.Where(grp => grp.Any(str => !grp.Key.TryConvertValue(str))).ToList();
            Check(invalidOptionValues.Count == 0, "the following options have invalid values: {0}", ToStr(invalidOptionValues));

            // check user validation rules
            var validationErrorMessages = ConfiguredOptionValues.SelectMany(grp => grp.Select(str => grp.Key.Validate(str))).Where(x => x != null).ToList();
            Check(validationErrorMessages.Count == 0, string.Join("\n", validationErrorMessages));
        }

        /// <summary>See <see cref="IParser.Render(Config)"/>.</summary>
        public string Render() => Parser.Render(this);

        [Pure]
        private static string ToStr(List<IGrouping<Option, string>> grps) => string.Join(", ", grps.Select(grp => grp.Key.LongName));

        [Pure]
        private static string ToStr(List<Option> opts) => string.Join(", ", opts.Select(o => o.LongName));

        private static ILookup<Option, string> ParseOptions(Queue<string> argsQueue, IParser parser,
            Dictionary<string, Option> longNameIndex, Dictionary<string, Option> shortNameIndex,
            bool ignoreInvalidOptions = false)
        {
            var diagnostics = new List<string>();

            var values = new LinkedList<Tuple<Option, string>>();
            var optionsQueue = new Queue<ParsedOption>();
            while (argsQueue.Count + optionsQueue.Count > 0)
            {
                var parsedOption = optionsQueue.Count > 0
                    ? optionsQueue.Dequeue()
                    : parser.Parse(argsQueue);
                var matchedOption =
                    parsedOption.PrefixKind == PrefixKind.Long ? LookUp(parsedOption.Key, longNameIndex) :
                    parsedOption.PrefixKind == PrefixKind.Short ? LookUp(parsedOption.Key, shortNameIndex) :
                    parsedOption.PrefixKind == PrefixKind.Either ? LookUp(parsedOption.Key, longNameIndex, shortNameIndex) :
                    parsedOption.PrefixKind == PrefixKind.None ? LookUp(parsedOption.Key, longNameIndex, shortNameIndex) :
                    null;
                if (matchedOption == null && !ignoreInvalidOptions)
                {
                    diagnostics.Add(Inv("Configuration flag '{0}' not found", parsedOption.Key));
                }
                else if (matchedOption != null)
                {
                    Add(values, matchedOption, parsedOption.Value);
                    optionsQueue.EnqueueAll(SafeExpand(matchedOption, parsedOption, parser, diagnostics));
                }
            }

            if (diagnostics.Any())
            {
                Check(false, PrintDiagnostics(diagnostics));
            }

            return values.ToLookup(tuple => tuple.Item1, tuple => tuple.Item2);
        }

        private static void Add(LinkedList<Tuple<Option, string>> values, Option matchedOption, string value)
        {
            var tuple = new Tuple<Option, string>(matchedOption, value);
            if (matchedOption.IsMultiValue)
            {
                values.AddLast(tuple);
                return;
            }

            var existingNode = FindNode(values.First, matchedOption.LongName);

            if (existingNode == null)
            {
                values.AddLast(tuple);
            }
            else
            {
                values.Remove(existingNode);
                values.AddLast(tuple);
            }
        }

        private static LinkedListNode<Tuple<Option, string>> FindNode(LinkedListNode<Tuple<Option, string>> node, string longName)
        {
            return
                node == null ? null :
                node.Value.Item1.LongName == longName ? node :
                FindNode(node.Next, longName);
        }

        [Pure]
        private static string PrintDiagnostics(List<string> diagnostics)
        {
            return string.Join(Environment.NewLine, diagnostics.Select(s => " *** " + s));
        }

        [Pure]
        private static IEnumerable<ParsedOption> SafeExpand(Option matchedOption, ParsedOption parsedOption, IParser parser, List<string> diagnostics)
        {
            try
            {
                return matchedOption.Expand(parsedOption.Value);
            }
            catch (Exception e)
            {
                diagnostics.Add(Inv("Could not expand option [[ {0} ]]. Error message: {1}", parser.RenderSingleOption(parsedOption), e.GetLogEventMessage()));
                return ParsedOption.EmptyCollection;
            }
        }

        [Pure]
        private static void CreateIndexes(IEnumerable<Option> confOptions, StringComparer comparer, out Dictionary<string, Option> longNameIndex, out Dictionary<string, Option> shortNameIndex)
        {
            longNameIndex = new Dictionary<string, Option>(comparer);
            shortNameIndex = new Dictionary<string, Option>(comparer);
            foreach (var opt in confOptions)
            {
                Check(!longNameIndex.ContainsKey(opt.LongName), "long-form option name '{0}' specified twice", opt.LongName);
                Check(!shortNameIndex.ContainsKey(opt.LongName), "long-form option name '{0}' found as a short-form name of another option", opt.LongName);
                longNameIndex[opt.LongName] = opt;

                if (opt.ShortName != null)
                {
                    Check(!shortNameIndex.ContainsKey(opt.ShortName), "short-form option name '{0}' specified twice", opt.LongName);
                    Check(!longNameIndex.ContainsKey(opt.ShortName), "short-form option name '{0}' found as a long-form name of another option", opt.LongName);
                    shortNameIndex[opt.ShortName] = opt;
                }
            }
        }

        [Pure]
        private static TV LookUp<TK, TV>(TK key, params Dictionary<TK, TV>[] indexes)
        {
            foreach (var index in indexes)
            {
                TV value;
                if (index.TryGetValue(key, out value))
                {
                    return value;
                }
            }

            return default(TV);
        }

        private static string Inv(string format, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }

        private static void Check(bool condition, string errorMessageFormat, params object[] errorMessageParams)
        {
            if (!condition)
            {
                throw new ArgumentException(Inv(errorMessageFormat, errorMessageParams));
            }
        }

        /// <summary>Initializer for all the fields (and nothing more).</summary>
        private Config(IParser parser, IReadOnlyCollection<Option> allConfigurationOptions, ILookup<Option, string> configuredOptions)
        {
            Parser = parser;
            AllConfigurationOptions = allConfigurationOptions;
            ConfiguredOptionValues = configuredOptions;
        }

        /// <summary>
        ///     Clones and modifies config according to the specified included or excluded options.
        /// </summary>
        public Config With(IEnumerable<KeyValuePair<Option, string>> includes = null, IEnumerable<Option> excludes = null)
        {
            if (includes == null && excludes == null)
            {
                return this;
            }

            var newAllConfigurationOptions = new HashSet<Option>(AllConfigurationOptions);
            var newConfiguredOptionsValues = ConfiguredOptionValues.ToDictionary(g => g.Key, g => g.FirstOrDefault());

            if (excludes != null)
            {
                foreach (var exclude in excludes)
                {
                    newAllConfigurationOptions.Remove(exclude);
                    newConfiguredOptionsValues.Remove(exclude);
                }
            }

            if (includes != null)
            {
                foreach (var include in includes)
                {
                    newAllConfigurationOptions.Add(include.Key);
                    newConfiguredOptionsValues[include.Key] = include.Value;
                }
            }

            return new Config(
                Parser,
                newAllConfigurationOptions.ToList(),
                newConfiguredOptionsValues.ToLookup(kvp => kvp.Key, kvp => kvp.Value));
        }
    }

    internal static class QueueExtensions
    {
        /// <summary>
        ///     Enqueues a collection of elements.
        /// </summary>
        public static void EnqueueAll<T>(this Queue<T> queue, IEnumerable<T> elements)
        {
            Contract.Requires(elements != null);

            foreach (var element in elements)
            {
                queue.Enqueue(element);
            }
        }
    }
}
