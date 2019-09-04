// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using BuildXL.Utilities;

namespace BuildXL.ToolSupport
{
    /// <summary>
    /// Support services to help command-line parsing.
    /// </summary>
    /// <remarks>
    /// This class automatically expands response file arguments and understands how to parse options
    /// in the /name:value syntax.
    /// Whenever this class fails and throws an exception, it always displays a suitable error
    /// to the user prior to returning so there is not need for further output.
    /// </remarks>
    public class CommandLineUtilities
    {
        /// <summary>
        /// The marker character to indicate a response file.
        /// </summary>
        private const char ResponseFilePrefix = '@';

        private readonly string[] m_args;

        /// <summary>
        /// /option:arg separator.
        /// </summary>
        private static readonly char[] s_separators = { ':' };

        /// <summary>
        /// Wraps a command-line for easy consumption.
        /// </summary>
        /// <param name="args">The command-line argument array typically obtained at program startup.</param>
        /// <remarks>
        /// This automatically expands any response files containing within the arguments.
        /// </remarks>
        public CommandLineUtilities(IReadOnlyCollection<string> args)
        {
            Contract.Requires(args != null);
            m_args = ExpandResponseFiles(args);
        }

        /// <summary>
        /// Gets the set of options associated with the current command-line.
        /// </summary>
        /// <remarks>
        /// Options are expressed using a /name:value syntax.
        /// </remarks>
        public IEnumerable<Option> Options
        {
            get
            {
                Contract.Ensures(Contract.Result<IEnumerable<Option>>() != null);

                foreach (string iteratorArg in m_args)
                {
                    string arg = iteratorArg;

                    if (arg.StartsWith("/", StringComparison.Ordinal))
                    {
                        string name;
                        string value;

                        int separatorIndex = arg.IndexOfAny(s_separators);
                        if (separatorIndex != -1)
                        {
                            name = arg.Substring(1, separatorIndex - 1);
                            value = arg.Substring(separatorIndex + 1);
                        }
                        else
                        {
                            name = arg.Substring(1);
                            value = string.Empty;
                        }

                        var opt = new Option { Name = name, Value = value };
                        yield return opt;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the set of unadorned arguments associated with the current command-line.
        /// </summary>
        /// <remarks>
        /// This eliminates all options from the command-line and returns what's left.
        /// </remarks>
        public IEnumerable<string> Arguments
        {
            get
            {
                Contract.Ensures(Contract.Result<IEnumerable<string>>() != null);

                foreach (string iteratorArg in m_args)
                {
                    string arg = iteratorArg;

                    if (!arg.StartsWith("/", StringComparison.Ordinal))
                    {
                        yield return arg;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the set of arguments from the current command-line with response file arguments expanded
        /// </summary>
        public IReadOnlyCollection<string> ExpandedArguments
        {
            get
            {
                Contract.Ensures(Contract.Result<IReadOnlyCollection<string>>() != null);
                return m_args;
            }
        }

        /// <summary>
        /// Parse an option that produces a string and fail if the option has already been encountered.
        /// </summary>
        public static string ParseSingletonStringOption(Option opt, string existingValue)
        {
            if (existingValue != null)
            {
                throw Error("The /{0} argument can only be provided once.", opt.Name);
            }

            return ParseStringOption(opt);
        }

        /// <summary>
        /// Parse an option that produces a string.
        /// </summary>
        public static string ParseStringOption(Option opt)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                throw Error("The /{0} argument requires a value.", opt.Name);
            }

            return opt.Value;
        }

        /// <summary>
        /// Parse an option that optionally produces a string and fail if the option has already been encountered.
        /// </summary>
        public static string ParseSingletonOptionalStringOption(Option opt, string existingValue)
        {
            if (existingValue != null)
            {
                throw Error("The /{0} argument can only be provided once.", opt.Name);
            }

            return ParseStringOptionalOption(opt);
        }

        /// <summary>
        /// Parse an option that optionally produces a string.
        /// </summary>
        public static string ParseStringOptionalOption(Option opt)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                return string.Empty;
            }

            return opt.Value;
        }

        /// <summary>
        /// Parse an option that parses a key value pair and adds it to a dictionary.
        /// </summary>
        public static void ParsePropertyOption(Option opt, Dictionary<string, string> map)
        {
            Contract.Requires(map != null);

            var keyValuePair = ParseKeyValuePair(opt);
            if (!string.IsNullOrEmpty(keyValuePair.Value))
            {
                map[keyValuePair.Key] = keyValuePair.Value;
            }
            else
            {
                // a blank property specified after an existing one should blank it out
                map.Remove(keyValuePair.Key);
            }
        }

        /// <summary>
        /// Parse an option that produces a Guid.
        /// </summary>
        public static Guid ParseGuidOption(Option opt)
        {
            string value = ParseStringOption(opt);
            Guid guidValue;
            if (Guid.TryParse(value, out guidValue))
            {
                return guidValue;
            }

            throw Error("The /{0} argument is not a legal GUID.", opt.Name);
        }

        /// <summary>
        /// Parse an option that produces an enum.
        /// </summary>
        public static TEnum ParseEnumOption<TEnum>(Option opt)
            where TEnum : struct
        {
            string value = ParseStringOption(opt);
            TEnum enumValue;
            if (Enum.TryParse(value, ignoreCase: true, result: out enumValue) && Enum.IsDefined(typeof(TEnum), enumValue))
            {
                return enumValue;
            }

            throw Error("The /{0} argument is not a legal value. Valid values are: {1}", opt.Name, string.Join(", ", Enum.GetNames(typeof(TEnum)).OrderBy(name => name)));
        }

        /// <summary>
        /// Parse an option that produces an enum, where the enum has values for true/false
        /// </summary>
        public static TEnum ParseBoolEnumOption<TEnum>(Option opt, bool boolSuffix, TEnum trueValue, TEnum falseValue)
            where TEnum : struct
        {
            if (!string.IsNullOrWhiteSpace(opt.Value))
            {
                TEnum enumValue;
                if (Enum.TryParse(opt.Value, ignoreCase: true, result: out enumValue) && Enum.IsDefined(typeof(TEnum), enumValue))
                {
                    return enumValue;
                }
                else
                {
                    throw Error("The /{0} argument is not a legal value. Valid values are: +,-,{1}", opt.Name, string.Join(", ", Enum.GetNames(typeof(TEnum))));
                }
            }

            return boolSuffix ? trueValue : falseValue;
        }

        /// <summary>
        /// Parse an option that produces a string and an optional bool suffix (i.e., /option:foo+, /option:foo-, /option:foo).
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "bool")]
        public static (string, bool) ParseStringOptionWithBoolSuffix(Option opt, bool boolDefault)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                throw Error("The /{0} argument requires a value.", opt.Name);
            }

            bool boolPart;
            string value = opt.Value;
            if (value.EndsWith("+", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
                boolPart = true;
            }
            else if (value.EndsWith("-", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 1);
                boolPart = false;
            }
            else
            {
                boolPart = boolDefault;
            }

            return (value, boolPart);
        }

        /// <summary>
        /// Parse an option that produces a path string and fail if the option has already been encountered.
        /// </summary>
        public static AbsolutePath ParseSingletonPathOption(Option opt, PathTable pathTable, AbsolutePath existingValue)
        {
            if (existingValue != AbsolutePath.Invalid)
            {
                throw Error("The /{0} argument can only be provided once.", opt.Name);
            }

            return ParsePathOption(opt, pathTable);
        }

        /// <summary>
        /// Parse an option that produces a path.
        /// </summary>
        public static AbsolutePath ParsePathOption(Option opt, PathTable pathTable)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                throw Error("The /{0} argument requires a value.", opt.Name);
            }

            // Create a full path in case it is relative
            return GetFullPath(opt.Value, opt, pathTable);
        }

        /// <summary>
        /// Parse a repeating option that produces a list of paths.
        /// </summary>
        public static IEnumerable<AbsolutePath> ParseRepeatingPathOption(Option opt, PathTable pathTable, string separator) => ParseRepeatingOption(opt, separator, v => GetFullPath(v, opt, pathTable));

        /// <summary>
        /// Gets the full path for an option
        /// </summary>
        public static AbsolutePath GetFullPath(string path, Option opt, PathTable pathTable)
        {
            return AbsolutePath.Create(pathTable, GetFullPath(path, opt));
        }

        /// <summary>
        /// Parse an option that produces a path string and fail if the option has already been encountered.
        /// </summary>
        public static string ParseSingletonPathOption(Option opt, string existingValue)
        {
            if (!string.IsNullOrEmpty(existingValue))
            {
                throw Error("The /{0} argument can only be provided once.", opt.Name);
            }

            return ParsePathOption(opt);
        }

        /// <summary>
        /// Parse an option that produces a path.
        /// </summary>
        public static string ParsePathOption(Option opt)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                throw Error("The /{0} argument requires a value.", opt.Name);
            }

            // Create a full path in case it is relative
            return GetFullPath(opt.Value, opt);
        }

        /// <summary>
        /// Parse a repeating option that produces a list of paths.
        /// </summary>
        public static IEnumerable<string> ParseRepeatingPathOption(Option opt, string separator) => ParseRepeatingOption(opt, separator, v => GetFullPath(v, opt));

        /// <summary>
        /// Gets the full path for an option
        /// </summary>
        public static string GetFullPath(string path, Option opt)
        {
            string fullPath = null;

            try
            {
                fullPath = Path.GetFullPath(TrimPathQuotation(path));
            }
            catch (ArgumentException ex)
            {
                throw Error("The /{0} argument value {1} cannot be represented as a full path: {2}", opt.Name, path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw Error("The /{0} argument value {1} cannot be represented as a full path: {2}", opt.Name, path, ex.Message);
            }
            catch (SecurityException ex)
            {
                throw Error("The /{0} argument value {1} cannot be represented as a full path: {2}", opt.Name, path, ex.Message);
            }
            catch (IOException ex)
            {
                throw Error("The /{0} argument value {1} cannot be represented as a full path: {2}", opt.Name, path, ex.Message);
            }

            return fullPath;
        }

        /// <summary>
        /// Parses repeating path atom option.
        /// </summary>
        public static IEnumerable<PathAtom> ParseRepeatingPathAtomOption(Option opt, StringTable stringTable, string separator)
        {
            return ParseRepeatingOption(
                opt,
                separator,
                v =>
                {
                    v = v.Trim();

                    if (!PathAtom.TryCreate(stringTable, v, out PathAtom result))
                    {
                        throw Error("The /{0} argument value {1} cannot be represented as a path atom", opt.Name, v);
                    }

                    return result;
                });
        }

        /// <summary>
        /// Parses a repeating option that produces a list of values.
        /// </summary>
        public static IEnumerable<T> ParseRepeatingOption<T>(Option opt, string separator, Func<string, T> valueProducer)
        {
            if (string.IsNullOrEmpty(opt.Value))
            {
                throw Error("The /{0} argument requires a value.", opt.Name);
            }

            foreach (string stringPart in opt.Value.Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return valueProducer(stringPart);
            }
        }

        /// <summary>
        /// Removes surrounding single or double quotes from a path
        /// </summary>
        public static string TrimPathQuotation(string path)
        {
            foreach (char c in new char[] { '"', '\'' })
            {
                if (path.Length > 2 && path[0] == c && path[path.Length - 1] == c)
                {
                    path = path.Substring(1, path.Length - 2);
                }
            }

            return path;
        }

        /// <summary>
        /// Parse an option that produces an int value.
        /// </summary>
        public static int ParseInt32Option(Option opt, int minValue, int maxValue)
        {
            Contract.Requires(minValue < maxValue);

            int result;
            if (!int.TryParse(opt.Value, out result) || (result < minValue) || (result > maxValue))
            {
                throw Error(
                    "The value provided for the /{0} argument is invalid, expecting an integer in the range {1}..{2} but got '{3}'.",
                    opt.Name,
                    minValue,
                    maxValue,
                    opt.Value);
            }

            return result;
        }

        /// <summary>
        /// Parse an option that produces a uint value.
        /// </summary>
        public static uint ParseUInt32Option(Option opt, uint minValue, uint maxValue)
        {
            Contract.Requires(minValue < maxValue);

            uint result;
            if (!uint.TryParse(opt.Value, out result) || (result < minValue) || (result > maxValue))
            {
                throw Error(
                    "The value provided for the /{0} argument is invalid, expecting an integer in the range {1}..{2} but got '{3}'.",
                    opt.Name,
                    minValue,
                    maxValue,
                    opt.Value);
            }

            return result;
        }

        /// <summary>
        /// Parse an option that produces an double value.
        /// </summary>
        public static double ParseDoubleOption(Option opt, double minValue, double maxValue)
        {
            Contract.Requires(minValue < maxValue);

            double result;
            if (!double.TryParse(opt.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                (result < minValue) ||
                (result > maxValue) ||
                double.IsNaN(result))
            {
                throw Error(
                    "The value provided for the /{0} argument is invalid, expecting a floating point value in the range {1}..{2} but got '{3}'.",
                    opt.Name,
                    minValue,
                    maxValue,
                    opt.Value);
            }

            return result;
        }

        /// <summary>
        /// Parse an option that produces an long value.
        /// </summary>
        public static long ParseInt64Option(Option opt, long minValue, long maxValue)
        {
            Contract.Requires(minValue < maxValue);

            long result;
            if (!long.TryParse(opt.Value, out result) || (result < minValue) || (result > maxValue))
            {
                throw Error(
                    "The value provided for the /{0} argument is invalid, expecting an integer in the range {1}..{2} but got '{3}'.",
                    opt.Name,
                    minValue,
                    maxValue,
                    opt.Value);
            }

            return result;
        }

        /// <summary>
        /// Parse an option that produces an boolean value.
        /// </summary>
        /// <remarks>
        /// Boolean values use the following syntax:
        /// /name      ; true
        /// /name+     ; true
        /// /name-     ; false
        /// /name:???  ; error
        /// </remarks>
        public static bool ParseBooleanOption(Option opt)
        {
            if (!string.IsNullOrEmpty(opt.Value))
            {
                throw Error("The value provided for the /{0} argument is invalid.", opt.Name);
            }

            return !opt.Name.EndsWith("-", StringComparison.Ordinal);
        }

        /// <summary>
        /// Parse an option that is a keyvalue pair and maintain the dictionary
        /// </summary>
        public static void ParseKeyValueOption(Dictionary<string, string> dictionary, Option opt)
        {
            Contract.Requires(dictionary != null);

            var keyValuePair = ParseKeyValuePair(opt);
            dictionary[keyValuePair.Key] = keyValuePair.Value;
        }

        /// <summary>
        /// Parse an option that is a key Value Pair
        /// </summary>
        /// <remarks>
        /// /name:key=value     ; adds or sets ("key", "value")
        /// /name:key=          ; adds or sets ("key", null)
        /// /name:key           ; adds or sets ("key", null)
        /// /name:key=1=2       ; adds or sets ("key", "1=2")
        /// /name:=             ; error
        /// /name               ; error
        /// /name:              ; error
        /// </remarks>
        public static KeyValuePair<string, string> ParseKeyValuePair(Option opt)
        {
            var value = opt.Value;
            if (string.IsNullOrEmpty(value))
            {
                throw Error("The value provided for the /{0} argument is invalid.", opt.Name);
            }

            var firstEquals = value.IndexOf('=');
            if (firstEquals == 0)
            {
                throw Error("The value '{0}' provided for the /{0} argument is invalid. It can't start with an '=' separator", value, opt.Name);
            }

            if (firstEquals < 0)
            {
                return new KeyValuePair<string, string>(value, null);
            }

            var key = value.Substring(0, firstEquals);
            if (firstEquals >= value.Length - 1)
            {
                return new KeyValuePair<string, string>(key, null);
            }
            else
            {
                return new KeyValuePair<string, string>(key, value.Substring(firstEquals + 1));
            }
        }

        /// <summary>
        /// Helper for reporting invalid option.
        /// </summary>
        public static void UnexpectedOption(Option opt)
        {
            throw Error("Unsupported command line argument {0} encountered.", opt.Name);
        }

        /// <summary>
        /// Parse an option that produces no value.
        /// </summary>
        public static void ParseVoidOption(Option opt)
        {
            if (!string.IsNullOrEmpty(opt.Value))
            {
                throw Error("No value is expected for the /{0} argument, but got '{1}'.", opt.Name, opt.Value);
            }
        }

        /// <summary>
        /// Parse an option that produces no value.
        /// </summary>
        public static void MissingRequiredOption(string optionName)
        {
            throw Error("Missing required command line argument /{0}.", optionName);
        }

        /// <summary>
        /// Produces an exception object.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes")]
        public static Exception Error(string format, params object[] args)
        {
            Contract.Requires(!string.IsNullOrEmpty(format));
            Contract.Requires(args != null);
            return new InvalidArgumentException(string.Format(CultureInfo.CurrentCulture, format, args));
        }

        /// <summary>
        /// Produces an exception object.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes")]
        public static Exception Error(Exception inner, string format, params object[] args)
        {
            Contract.Requires(!string.IsNullOrEmpty(format));
            Contract.Requires(args != null);
            return new InvalidArgumentException(string.Format(CultureInfo.CurrentCulture, format, args), inner);
        }

        private static string[] ExpandResponseFiles(IReadOnlyCollection<string> args)
        {
            Contract.Requires(args != null);

            bool found = false;
            foreach (string arg in args)
            {
                if (arg[0] == ResponseFilePrefix)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return args.ToArray();
            }

            using (var wrapper = Pools.GetStringList())
            {
                var resultArgs = wrapper.Instance;
                foreach (string arg in args)
                {
                    if (arg[0] == ResponseFilePrefix)
                    {
                        string responseFileName = arg.Substring(1);

                        string responseFileText;
                        try
                        {
                            responseFileText = File.ReadAllText(responseFileName, Encoding.UTF8);
                        }
                        catch (Exception ex)
                        {
                            throw Error(ex, "Error while reading the response file '{0}': {1}.", responseFileName, ex.Message);
                        }

                        string[] responseFileArgs = responseFileText.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        resultArgs.AddRange(responseFileArgs);
                    }
                    else
                    {
                        resultArgs.Add(arg);
                    }
                }

                return resultArgs.ToArray();
            }
        }

        /// <summary>
        /// A single command-line option.
        /// </summary>
        public struct Option : IEquatable<Option>
        {
            /// <summary>
            /// The name of the option.
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// The optional value for the option. This may be null or string.Empty.
            /// </summary>
            public string Value { get; set; }

            /// <inheritdoc />
            public bool Equals(Option other)
            {
                return Name == other.Name
                       && Value == other.Value;
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return StructUtilities.Equals(this, obj);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                int hash = 0;
                if (Name != null)
                {
                    hash = Name.GetHashCode();
                }

                if (Value != null)
                {
                    hash ^= Value.GetHashCode();
                }

                return hash;
            }

            /// <summary>
            /// Equality operator for two contexts.
            /// </summary>
            public static bool operator ==(Option left, Option right)
            {
                return left.Equals(right);
            }

            /// <summary>
            /// Inequality operator for two contexts.
            /// </summary>
            public static bool operator !=(Option left, Option right)
            {
                return !left.Equals(right);
            }

            /// <summary>
            /// Returns the Option as a string.
            /// </summary>
            public string PrintCommandLineString()
            {
                var builder = new StringBuilder();
                PrintCommandLineString(builder);
                return builder.ToString();
            }

            /// <summary>
            /// Appends the Option as a string to the given builder.
            /// </summary>
            public void PrintCommandLineString(StringBuilder builder)
            {
                if (!string.IsNullOrEmpty(Name))
                {
                    builder.Append("/");
                    builder.Append(Name);
                }

                if (!string.IsNullOrEmpty(Value))
                {
                    builder.Append(":");
                    PrintEncodeArgument(builder, Value);
                }
            }

            /// <summary>
            /// Simple encoding of commandline arguments
            /// </summary>
            public static void PrintEncodeArgument(StringBuilder builder, string argument)
            {
                if (string.IsNullOrEmpty(argument))
                {
                    return;
                }

                bool hasSpace = argument.Contains(" ");
                if (hasSpace)
                {
                    builder.Append("\"");
                }

                // escape double quotes
                builder.Append(argument.Replace("\"", "\\\""));

                if (hasSpace)
                {
                    builder.Append("\"");
                }
            }
        }
    }
}
