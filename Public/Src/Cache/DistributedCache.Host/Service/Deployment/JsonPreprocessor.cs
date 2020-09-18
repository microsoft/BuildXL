// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Defines constraint specifying and name and possible matching values for constraints with the given name
    /// </summary>
    public class ConstraintDefinition
    {
        /// <nodoc />
        public ConstraintDefinition(string name, IEnumerable<string> acceptableValues)
        {
            Name = name;
            AcceptableValues = acceptableValues;
        }

        /// <summary>
        /// The name that the constraint will have in the json document.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The values that this constraint can be evaluated to. Other values will be filtered out.
        /// </summary>
        public IEnumerable<string> AcceptableValues { get; }
    }

    /// <summary>
    /// Pre-processes allowing config values to be defined as a function of replacement macros, and 
    /// allowing for config values to be overriden depending on user-defined constraints.
    /// Example:
    /// {
    ///   "Connection"                    : "Conn.{RegionId}.{StampId:L}",
    ///   "Connection[RegiojsonProcessed  : "Conn.overridden",
    ///   "Connection[Stamp:DM_S1|DM_S3]" : "Conn.overridden.For.{StampId}",
    /// }
    /// For, say, SN_S1 "Connection" evaluates as "Conn.SN.sn_s1". The ":L" suffix converts 
    /// the macro expansion to lower-case.  For stamps in region DM, it evaluates as "Conn.overridden".
    /// Except for stamps DM_S1 and DM_S3, which are further overridden to "Conn.overridden.For.DM_S1" 
    /// and "Conn.overridden.For.DM_S1".
    /// (Stamp matches take precedence over Region matches.)
    /// Replacement macros and constraints are defined in anprivate class JsonPreprocessorContext
    public class JsonPreprocessor
    {
        private readonly Dictionary<string, ConstraintDefinition> _constraintsByName = new Dictionary<string, ConstraintDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReplacementMacro> _replacementMacros = new Dictionary<string, ReplacementMacro>(StringComparer.OrdinalIgnoreCase);

        private static Regex NameAndConstraintsRegex { get; } = new Regex($@"^(?<baseName>.*?)\s*(?<constraint>{Constraint.RegexPattern})+$");

        public JsonPreprocessor(IEnumerable<ConstraintDefinition> constraintDefinitions, IReadOnlyDictionary<string, string> replacementMacros)
        {
            foreach (var constraintDefinition in constraintDefinitions)
            {
                _constraintsByName[constraintDefinition.Name] = constraintDefinition;
            }

            foreach (var entry in replacementMacros)
            {
                _replacementMacros[entry.Key] = new ReplacementMacro(entry.Key, entry.Value);
            }
        }

        public string Preprocess(string json)
        {
            using (var document = JsonDocument.Parse(json, DeploymentUtilities.ConfigurationDocumentOptions))
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions() { Indented = true }))
                {
                    PreprocessJsonElement(document.RootElement, writer);
                }

                stream.Position = 0;

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private void PreprocessJsonElement(JsonElement value, Utf8JsonWriter writer)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    PreprocessJObject(value, writer);
                    return;
                case JsonValueKind.Array:
                    PreprocessJArray(value, writer);
                    return;
                case JsonValueKind.String:
                    writer.WriteStringValue(PreprocessString(value.GetString()));
                    return;
                default:
                    value.WriteTo(writer);
                    return;
            }
        }

        private void PreprocessJObject(JsonElement obj, Utf8JsonWriter writer)
        {
            writer.WriteStartObject();

            var properties = obj.EnumerateObject().ToList();
            var newObj = new Dictionary<string, JsonElement>();

            foreach (var property in properties)
            {
                var allowed = TryEvaluateNameWithConstraints(property, out var newName);
                if (allowed)
                {
                    // All constraints are satisfied.
                    newObj[newName] = property.Value;
                }
            }

            foreach (var property in newObj)
            {
                writer.WritePropertyName(property.Key);

                // Preprocess and write property value
                PreprocessJsonElement(property.Value, writer);
            }

            writer.WriteEndObject();
        }

        private void PreprocessJArray(JsonElement array, Utf8JsonWriter writer)
        {
            writer.WriteStartArray();

            foreach (var item in array.EnumerateArray())
            {
                PreprocessJsonElement(item, writer);
            }

            writer.WriteEndArray();
        }

        private string PreprocessString(string s)
        {
            if (ReplacementMacro.HasPossibleReplacements(s))
            {
                foreach (var replacementMacro in _replacementMacros.Values)
                {
                    s = replacementMacro.Apply(s);
                }
            }

            return s;
        }

        private bool TryEvaluateNameWithConstraints(JsonProperty property, out string newName)
        {
            ParseNameAndConstraints(property, out newName, out var constraints);
            if (string.IsNullOrEmpty(newName))
            {
                Error(property.Name, "Processed field name is empty or white-space");
            }

            foreach (var constraint in constraints)
            {
                if (constraint.Candidates.Any(c => string.IsNullOrWhiteSpace(c.Value)))
                {
                    Error(property.Name, $"Some term in {constraint.Name} constraint is empty.");
                }

                if (_constraintsByName.TryGetValue(constraint.Name, out var constraintDefinition))
                {
                    if (!constraint.Match(constraintDefinition))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private void ParseNameAndConstraints(JsonProperty property, out string name, out IEnumerable<Constraint> constraints)
        {
            var match = NameAndConstraintsRegex.Match(property.Name);
            if (!match.Success)
            {
                name = PreprocessString(property.Name.Trim());
                constraints = Enumerable.Empty<Constraint>();
                return;
            }

            name = PreprocessString(match.Groups["baseName"].Value);
            constraints = match.Groups["constraint"].Captures.Cast<Capture>()
                .Select(c => new Constraint(c.Value))
                .ToArray();
        }

        private void Error(string name, string message)
        {
            throw new FormatException($"Error at '{name}': {message}");
        }

        /// <summary>
        /// Represents a constraint applied to a json property 
        /// 
        /// Examples:
        /// {
        ///     "MyProp1 [Stamp:S1|S2]" --> constraint (Name="Stamp", Candidates=["S1", "S2"])
        ///     "MyProp2 [!Stamp:S1|S2]" --> constraint (Negated=true, Name="Stamp", Candidates=["S1", "S2"])
        /// }
        /// </summary>
        private class Constraint
        {
            public static readonly string RegexPattern = @"\[(?<negation>!)?(?<constraintName>\w+):(?<candidates>[!\.\w\|]*)\]";
            private static readonly char[] ConstraintCandidateSeparator = new[] { '|' };
            private static Regex ConstraintRegex { get; } = new Regex($@"^{RegexPattern}$");

            public bool Negated { get; }
            public string Name { get; }
            public ConstraintCandidate[] Candidates { get; }

            public Constraint(string constraintSpecifier)
            {
                var match = ConstraintRegex.Match(constraintSpecifier);
                Name = match.Groups["constraintName"].Value;
                Negated = match.Groups["negation"].Success;
                Candidates = match.Groups["candidates"].Value
                    .Split(ConstraintCandidateSeparator)
                    .Select(c => new ConstraintCandidate(c))
                    .ToArray();
            }

            public bool Match(ConstraintDefinition definition)
            {
                foreach (var value in definition.AcceptableValues)
                {
                    if (Candidates.Any(c => c.Match(value)))
                    {
                        // One of the values matches the constraint so
                        // return a match (or no match if the constraint is negated)
                        return !Negated;
                    }
                }

                if (!definition.AcceptableValues.Any() && Candidates.Any(c => c.Negated))
                {
                    // When there are no values specified, any negated constraints should be considered
                    // a match
                    return !Negated;
                }

                // None of the values match the constraint
                // return no match (or a match if negated)
                return Negated;
            }
        }

        /// <summary>
        /// See <see cref="Constraint"/>
        /// </summary>
        private class ConstraintCandidate
        {
            public bool Negated { get; }
            public string Value { get; }

            public ConstraintCandidate(string candidateSpecifier)
            {
                Negated = candidateSpecifier.StartsWith("!", StringComparison.OrdinalIgnoreCase);

                // Skip negation character if needed
                Value = candidateSpecifier.Substring(Negated ? 1 : 0);
            }

            public bool Match(string value)
            {
                var valueMatches = string.Equals(Value, value, StringComparison.OrdinalIgnoreCase);
                return Negated ? !valueMatches : valueMatches;
            }
        }

        /// <summary>
        /// Logic to replace macros with stamp-/region-specific actual values.
        /// </summary>
        private class ReplacementMacro
        {
            // Forms regexes that allow for strings of forms: {RegionId}, {RegionId:L}, {RegionId:U}
            private const string RegexFormat = @"{{(?<macro>{0})(:(?<modifiers>[LUA]+))?}}";
            private readonly Regex _regex;
            private readonly string _replacementValue;
            private readonly string _macroText;

            public ReplacementMacro(string macroText, string replacementValue)
            {
                _macroText = macroText;
                _regex = new Regex(string.Format(RegexFormat, macroText),
                    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                _replacementValue = replacementValue;//.ThrowIfNull(nameof(replacementValue));
            }

            // matches anything that is not alpha-numeric
            private static readonly Regex RxNonAlpha = new Regex(@"[^A-Za-z0-9]+");

            public static bool HasPossibleReplacements(string str)
            {
                return str.Contains("{") && str.Contains("}");
            }

            public string Apply(string str)
            {
                return _regex.Replace(str, match =>
                {
                    string replacementValue = _replacementValue;

                    Group modifiersGroup = match.Groups["modifiers"];
                    if (modifiersGroup.Success)
                    {
                        if (modifiersGroup.Value.IndexOf("U", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            replacementValue = replacementValue.ToUpper();
                        }
                        else if (modifiersGroup.Value.IndexOf("L", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            replacementValue = replacementValue.ToLower();
                        }
                        if (modifiersGroup.Value.IndexOf("A", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            replacementValue = RxNonAlpha.Replace(replacementValue, "");
                        }
                    }

                    return replacementValue;
                });
            }
        }
    }
}
