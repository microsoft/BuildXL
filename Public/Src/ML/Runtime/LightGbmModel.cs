// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BuildXL.ML.Runtime
{
    /// <summary>
    /// Dependency-free evaluator for a LightGBM <c>Booster.dump_model()</c> JSON payload.
    /// Model payload packages supply schemas, features, and embedded weights; this runtime contains no trained data.
    /// </summary>
    internal sealed class LightGbmModel
    {
        private readonly string[] m_featureNames;
        private readonly Node[] m_trees;

        private LightGbmModel(string[] featureNames, Node[] trees)
        {
            m_featureNames = featureNames;
            m_trees = trees;
        }

        public IReadOnlyList<string> FeatureNames => m_featureNames;

        public double PredictRaw(ReadOnlySpan<double> features)
        {
            double sum = 0.0;
            for (int i = 0; i < m_trees.Length; i++)
            {
                sum += m_trees[i].Eval(features);
            }

            return sum;
        }

        public static LightGbmModel Parse(Stream jsonStream)
        {
            using (JsonDocument doc = JsonDocument.Parse(jsonStream))
            {
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("feature_names", out JsonElement featureNamesElement) || featureNamesElement.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("LightGBM model JSON is missing a 'feature_names' array.");
                }

                var featureNames = new string[featureNamesElement.GetArrayLength()];
                if (featureNames.Length == 0)
                {
                    throw new FormatException("LightGBM model JSON has an empty 'feature_names' array.");
                }

                var uniqueFeatureNames = new HashSet<string>(StringComparer.Ordinal);
                int featureIndex = 0;
                foreach (JsonElement name in featureNamesElement.EnumerateArray())
                {
                    string featureName = name.GetString();
                    if (string.IsNullOrEmpty(featureName) || !uniqueFeatureNames.Add(featureName))
                    {
                        throw new FormatException($"LightGBM model JSON contains invalid or duplicate feature name '{featureName ?? "<missing>"}'.");
                    }

                    featureNames[featureIndex++] = featureName;
                }

                if (!root.TryGetProperty("tree_info", out JsonElement treeInfoElement) || treeInfoElement.ValueKind != JsonValueKind.Array)
                {
                    throw new FormatException("LightGBM model JSON is missing a 'tree_info' array.");
                }

                var trees = new Node[treeInfoElement.GetArrayLength()];
                if (trees.Length == 0)
                {
                    throw new FormatException("LightGBM model JSON has an empty 'tree_info' array.");
                }

                int treeIndex = 0;
                foreach (JsonElement tree in treeInfoElement.EnumerateArray())
                {
                    if (!tree.TryGetProperty("tree_structure", out JsonElement structure))
                    {
                        throw new FormatException("A LightGBM 'tree_info' entry is missing 'tree_structure'.");
                    }

                    trees[treeIndex++] = ParseNode(structure, featureNames.Length);
                }

                return new LightGbmModel(featureNames, trees);
            }
        }

        private static Node ParseNode(JsonElement node, int featureCount)
        {
            if (node.TryGetProperty("leaf_value", out JsonElement leafValue))
            {
                return new Leaf(leafValue.GetDouble());
            }

            int feature = node.GetProperty("split_feature").GetInt32();
            if (feature < 0 || feature >= featureCount)
            {
                throw new FormatException($"LightGBM split feature index {feature} is outside the feature schema.");
            }

            string decisionType = node.GetProperty("decision_type").GetString();
            bool defaultLeft = node.TryGetProperty("default_left", out JsonElement defaultLeftElement) && defaultLeftElement.GetBoolean();
            Node left = ParseNode(node.GetProperty("left_child"), featureCount);
            Node right = ParseNode(node.GetProperty("right_child"), featureCount);
            JsonElement threshold = node.GetProperty("threshold");

            if (decisionType == "==")
            {
                var categories = new HashSet<int>();
                string[] values = threshold.ValueKind == JsonValueKind.String
                    ? threshold.GetString().Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
                    : new[] { threshold.GetInt32().ToString(CultureInfo.InvariantCulture) };
                foreach (string value in values)
                {
                    categories.Add(int.Parse(value, CultureInfo.InvariantCulture));
                }

                return new CategoricalSplit(feature, categories, defaultLeft, left, right);
            }

            if (decisionType == "<=")
            {
                double numericThreshold = threshold.ValueKind == JsonValueKind.String
                    ? double.Parse(threshold.GetString(), CultureInfo.InvariantCulture)
                    : threshold.GetDouble();
                return new NumericSplit(feature, numericThreshold, defaultLeft, left, right);
            }

            throw new FormatException($"Unsupported LightGBM decision_type '{decisionType}'.");
        }

        private abstract class Node
        {
            public abstract double Eval(ReadOnlySpan<double> features);
        }

        private sealed class Leaf : Node
        {
            private readonly double m_value;
            public Leaf(double value) => m_value = value;
            public override double Eval(ReadOnlySpan<double> features) => m_value;
        }

        private sealed class NumericSplit : Node
        {
            private readonly int m_feature;
            private readonly double m_threshold;
            private readonly bool m_defaultLeft;
            private readonly Node m_left;
            private readonly Node m_right;

            public NumericSplit(int feature, double threshold, bool defaultLeft, Node left, Node right)
            {
                m_feature = feature;
                m_threshold = threshold;
                m_defaultLeft = defaultLeft;
                m_left = left;
                m_right = right;
            }

            public override double Eval(ReadOnlySpan<double> features)
            {
                double value = features[m_feature];
                bool goLeft = double.IsNaN(value) ? m_defaultLeft : value <= m_threshold;
                return (goLeft ? m_left : m_right).Eval(features);
            }
        }

        private sealed class CategoricalSplit : Node
        {
            private readonly int m_feature;
            private readonly HashSet<int> m_categories;
            private readonly bool m_defaultLeft;
            private readonly Node m_left;
            private readonly Node m_right;

            public CategoricalSplit(int feature, HashSet<int> categories, bool defaultLeft, Node left, Node right)
            {
                m_feature = feature;
                m_categories = categories;
                m_defaultLeft = defaultLeft;
                m_left = left;
                m_right = right;
            }

            public override double Eval(ReadOnlySpan<double> features)
            {
                double value = features[m_feature];
                bool goLeft = double.IsNaN(value) ? m_defaultLeft : m_categories.Contains((int)value);
                return (goLeft ? m_left : m_right).Eval(features);
            }
        }
    }
}
