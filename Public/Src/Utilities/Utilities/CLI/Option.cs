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
    ///     Expands a single option into a number of <see cref="ParsedOption"/>s.
    /// </summary>
    public delegate IEnumerable<ParsedOption> Expander(string rawValue);

    /// <summary>
    ///     Converts a string to a given type.  Must be deterministic.
    /// </summary>
    /// <typeparam name="TValue">Type to convert to.</typeparam>
    /// <param name="rawValue">String to convert.</param>
    public delegate TValue Converter<TValue>(string rawValue);

    /// <summary>
    ///     Performs further validation.
    /// </summary>
    /// <typeparam name="TValue">Type of the value.</typeparam>
    /// <param name="value">Configured value.</param>
    /// <returns>Retuns an erro message or null to indicate that the value is valid.</returns>
    public delegate string Validator<TValue>(TValue value);

    /// <summary>
    ///     Base class for specifying command-line options.
    ///
    ///     An option should be uniquely identifiable by its <see cref="LongName"/>, which is, thus,
    ///     used for computing hash code and checking equality.
    /// </summary>
    /// <remarks>
    ///     Strongly immutable.
    /// </remarks>
    public class Option
    {
        /// <summary>Long name.</summary>
        public string LongName { get; }

        /// <summary>Short name.</summary>
        public string ShortName { get; set; }

        /// <summary>Description to be printed as part of a 'help' message.</summary>
        public string HelpText { get; set; }

        /// <summary>Whether this option must be specified.</summary>
        public bool IsRequired { get; set; }

        /// <summary>Whether this option can be specified multiple times.</summary>
        public bool IsMultiValue { get; set; }

        /// <summary>Whether this option value should be expanded into a number of options.</summary>
        public Expander Expander { get; set; }

        /// <summary>Returns whether a given string is a valid value for this option.</summary>
        public virtual bool TryConvertValue(string value) => true;

        /// <summary>Performs any user validation that may be set. Non-null return value indicates error message.</summary>
        public virtual string Validate(string value) => null;

        /// <summary>Type of the option value.</summary>
        public virtual Type ValueType => typeof(string);

        /// <summary>A single option value may be expanded into a number of option/value pairs (e.g., when reading additional values from a config file).</summary>
        public virtual IEnumerable<ParsedOption> Expand(string value)
        {
            Contract.Ensures(Contract.Result<IEnumerable<ParsedOption>>() != null);

            return Expander != null ? Expander(value) : ParsedOption.EmptyCollection;
        }

        /// <summary>Initializer for all the fields (and nothing more).</summary>
        public Option(string longName)
        {
            Contract.Requires(longName != null);

            LongName = longName;
        }

        /// <summary>Copy constructor</summary>
        public Option(Option clone)
            : this(clone.LongName)
        {
            ShortName = clone.ShortName;
            HelpText = clone.HelpText;
            IsRequired = clone.IsRequired;
            IsMultiValue = clone.IsMultiValue;
            Expander = clone.Expander;
        }

        /// <inheritdoc/>
        /// <remarks>The only contributing property is <see cref="LongName"/>.</remarks>
        public override int GetHashCode() => LongName.GetHashCode();

        /// <inheritdoc/>
        /// <remarks>The only contributing property is <see cref="LongName"/>.</remarks>
        public override bool Equals(object other)
        {
            var otherAsOption = other as Option;
            return otherAsOption != null && LongName == otherAsOption.LongName;
        }
    }

    /// <summary>
    ///     Command-line option parameterized by the type of a value it may hold.
    /// </summary>
    /// <remarks>
    ///     Strongly immutable.
    /// </remarks>
    /// <typeparam name="TValue">Type of the value that this option holds.</typeparam>
    public class Option<TValue> : Option
    {
        /// <summary>Converter from string to an instance of <typeparamref name="TValue"/>.</summary>
        public Converter<TValue> Converter { get; set; }

        /// <summary>Optional further validation of the configured value.</summary>
        public Validator<TValue> Validator { get; set; }

        /// <summary>Default value to use when no value is specified by the user.</summary>
        public TValue DefaultValue { get; set; }

        /// <summary>Initializer for all the fields (and nothing more).</summary>
        public Option(Converter<TValue> converter, string longName)
            : base(longName)
        {
            Contract.Requires(converter != null);

            Converter = converter;
        }

        /// <summary>Copy constructor</summary>
        public Option(Option<TValue> clone)
            : base(clone)
        {
            Contract.Requires(clone != null);

            Converter = clone.Converter;
            Validator = clone.Validator;
            DefaultValue = clone.DefaultValue;
        }

        /// <summary>
        ///     Returns the first value specified for this option in a given <see cref="Config"/>,
        ///     or <see cref="DefaultValue"/> if no value is found.
        /// </summary>
        /// <remarks>
        ///     Throws if <see cref="Converter"/> throws while trying to convert a found value to <typeparamref name="TValue"/>.
        ///     Calling <see cref="Config.Validate"/> beforehand ensures that all specified options have valid
        ///     values, and thus, this method won't fail.
        /// </remarks>
        [Pure]
        public TValue GetValue(Config conf)
        {
            var values = conf.ConfiguredOptionValues[this];
            return values.Any()
                ? Converter(values.First())
                : DefaultValue;
        }

        /// <summary>
        ///     Returns all the values specified for this option in a given <see cref="Config"/>.
        /// </summary>
        /// <remarks>
        ///     See remarks for <see cref="GetValue(Config)"/> for exceptions that can possibly be thrown.
        /// </remarks>
        [Pure]
        public IEnumerable<TValue> GetValues(Config conf)
        {
            return conf.ConfiguredOptionValues[this].Select(val => Converter(val));
        }

        /// <summary>
        ///     Returns whether the <paramref name="value"/> string represents a valid value for this option.
        /// </summary>
        [Pure]
        public override bool TryConvertValue(string value)
        {
            try
            {
                Converter(value);
                return true;
            }
#pragma warning disable ERP022
            catch
            {
                return false;
            }
#pragma warning restore ERP022
        }

        /// <inheritdoc />
        [Pure]
        public override string Validate(string value)
        {
            return Validator?.Invoke(Converter(value));
        }

        /// <inheritdoc />
        [Pure]
        public override Type ValueType => typeof(TValue);
    }

    /// <summary>Option of type <see cref="int"/>.</summary>
    public sealed class IntOption : Option<int>
    {
        /// <nodoc/>
        public static readonly Converter<int> IntConverter = (str) => int.Parse(str, CultureInfo.InvariantCulture);

        /// <nodoc/>
        public IntOption(string longName)
            : base(IntConverter, longName) { }
    }

    /// <summary>Option of type <see cref="string"/>.</summary>
    public sealed class StrOption : Option<string>
    {
        /// <nodoc/>
        public static readonly Converter<string> StrConverter = (str) => str;

        /// <nodoc/>
        public StrOption(string longName)
            : base(StrConverter, longName) { }
    }

    /// <summary>Option of type <see cref="Uri"/>.</summary>
    public sealed class UriOption : Option<Uri>
    {
        /// <nodoc/>
        public static readonly Converter<Uri> UriConverter = (str) => new Uri(str);

        /// <nodoc/>
        public UriOption(string longName)
            : base(UriConverter, longName) { }
    }

    /// <summary>Option of type <see cref="bool"/>.</summary>
    public sealed class BoolOption : Option<bool>
    {
        /// <nodoc/>
        public static readonly Converter<bool> BoolConverter = (str) => string.IsNullOrEmpty(str) || bool.Parse(str);

        /// <nodoc/>
        public BoolOption(string longName)
            : base(BoolConverter, longName) { }
    }

    /// <summary>Option of type <see cref="Nullable"/> <see cref="int"/>.</summary>
    public sealed class NullableIntOption : Option<int?>
    {
        /// <nodoc/>
        public static readonly Converter<int?> NullableIntConverter = (str) => str != null ? (int?)int.Parse(str, CultureInfo.InvariantCulture) : null;

        /// <nodoc/>
        public NullableIntOption(string longName)
            : base(NullableIntConverter, longName) { }
    }

    /// <summary>Option of type <see cref="Nullable"/> <see cref="bool"/>.</summary>
    public sealed class NullableBoolOption : Option<bool?>
    {
        /// <nodoc/>
        public static readonly Converter<bool?> NullableBoolConverter = (str) => str != null ? (bool?)(str == "true") : null;

        /// <nodoc/>
        public NullableBoolOption(string longName)
            : base(NullableBoolConverter, longName) { }
    }
}
