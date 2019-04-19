// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Helper class to create Html elements
    /// </summary>
    public class HtmlHelper
    {
        private readonly PathTable m_pathTable;
        private readonly StringTable m_stringTable;
        private readonly SymbolTable m_symbolTable;
        private readonly PipTable m_pipTable;

        /// <nodoc />
        public HtmlHelper(PathTable pathTable, StringTable stringTable, SymbolTable symbolTable, PipTable pipTable)
        {
            m_pathTable = pathTable;
            m_stringTable = stringTable;
            m_symbolTable = symbolTable;
            m_pipTable = pipTable;
        }

        public XDocument CreatePage(string title, XElement main)
        {
            return new XDocument(
                new XElement(
                    "html",
                    new XElement(
                        "head",
                        new XElement(
                            "style",
                            new XAttribute("type", "text/css"),
                            @"
.keyCol {
    display: inline-block; 
    width:250px;
    vertical-align: top;
}
.valCol {
    display: inline-block; 
}
.miniGroup {
    padding-bottom: 12px;
    padding-top: 12px;
}
.warning {
    padding-left: 12px;
    padding-bottom: 6px;
    border-left: 4px solid orange;
    border-bottom: 1px solid orange;
}
.error {
    padding-left: 12px;
    padding-bottom: 6px;
    border-left: 4px solid red;
    border-bottom: 1px solid red;
}
")),
                    new XElement(
                        "body",
                        new XElement("h1", title),
                        main)));
        }

        public XElement CreateBlock(string title, params object[] contents)
        {
            if (contents.Any(c => c != null))
            {
                return new XElement(
                    "div",
                    new XElement("h2", title),
                    new XElement("div", contents));
            }

            return null;
        }

        public XElement CreateRowInternal(string key, object value)
        {
            return new XElement(
                "div",
                new XElement("span", new XAttribute("class", "keyCol"), key),
                new XElement("span", new XAttribute("class", "valCol"), value));
        }

        public XElement CreateRow(string key, XElement value)
        {
            if (value == null)
            {
                return null;
            }

            return CreateRowInternal(key, value);
        }

        public XElement CreateRow(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            return CreateRowInternal(key, value.Replace("\b", "\\b"));
        }

        public XElement CreateEnumRow<T>(string key, T value) where T : struct, IComparable, IFormattable, IConvertible
        {
            if (!typeof(T).IsEnum)
            {
                throw new ArgumentException("T must be an enumerated type");
            }

            return CreateRowInternal(key, Enum.Format(typeof(T), value, "f"));
        }

        public XElement CreateRow(string key, DateTime value)
        {
            return CreateRowInternal(key, value.ToLongTimeString());
        }

        public XElement CreateRow(string key, TimeSpan? value)
        {
            if (value == null)
            {
                return CreateRowInternal(key, $"no time");
            }

            var ts = value.Value;
            if (ts.Days > 0)
            {
                return CreateRowInternal(key, $"{ts.Days} days, {ts.Hours} hours, {ts.Minutes} minutes");
            }

            if (ts.Hours > 0)
            {
                return CreateRowInternal(key, $"{ts.Hours} hours, {ts.Minutes} minutes, {ts.Seconds} seconds");
            }

            if (ts.Minutes > 0)
            {
                return CreateRowInternal(key, $"{ts.Minutes} minutes, {ts.Seconds} seconds, {ts.Milliseconds} ms");
            }

            if (ts.Seconds > 0)
            {
                return CreateRowInternal(key, $"{ts.Seconds} seconds, {ts.Milliseconds} ms");
            }

            return CreateRowInternal(key, $"{ts.Milliseconds} ms");
        }

        public XElement CreateRow(string key, bool value)
        {
            return CreateRow(key, value ? "true" : "false");
        }

        public XElement CreateRow(string key, double value)
        {
            return CreateRow(key, value.ToString("N3"));
        }

        public XElement CreateRow(string key, int value)
        {
            return CreateRow(key, value.ToString("N0", CultureInfo.InvariantCulture));
        }

        public XElement CreateRow(string key, long value)
        {
            return CreateRow(key, value.ToString("N0", CultureInfo.InvariantCulture));
        }

        public XElement CreateRow(string key, IEnumerable<string> values, bool sortEntries = true)
        {
            if (!values.Any())
            {
                return null;
            }

            return sortEntries ? CreateRow(key, new XElement("div", values.OrderBy(x => x).Select(value => new XElement("div", value))))
                               : CreateRow(key, new XElement("div", values.Select(value => new XElement("div", value))));
        }

        public XElement CreateRow(string key, FileArtifact value)
        {
            return value.IsValid ? CreateRow(key, value.Path.ToString(m_pathTable)) : null;
        }

        public XElement CreateRow(string key, IEnumerable<FileArtifact> values)
        {
            return CreateRow(key, values.Select(value => value.Path.ToString(m_pathTable)));
        }

        public XElement CreateRow(string key, DirectoryArtifact value)
        {
            return value.IsValid ? CreateRow(key, value.Path.ToString(m_pathTable)) : null;
        }

        public XElement CreateRow(string key, IEnumerable<DirectoryArtifact> values)
        {
            return CreateRow(key, values.Select(value => value.Path.ToString(m_pathTable)));
        }

        public XElement CreateRow(string key, PipData value)
        {
            return value.IsValid ? CreateRow(key, value.ToString(m_pathTable)) : null;
        }

        public XElement CreateRow(string key, AbsolutePath value)
        {
            return value.IsValid ? CreateRow(key, value.ToString(m_pathTable)) : null;
        }

        public XElement CreateRow(string key, IEnumerable<AbsolutePath> values)
        {
            return CreateRow(key, values.Select(value => value.ToString(m_pathTable)));
        }

        public XElement CreateRow(string key, PathAtom value)
        {
            return value.IsValid ? CreateRow(key, value.ToString(m_stringTable)) : null;
        }

        public XElement CreateRow(string key, StringId value)
        {
            return value.IsValid ? CreateRow(key, value.ToString(m_stringTable)) : null;
        }

        public XElement CreateRow(string key, LocationData value)
        {
            return CreateRow(key, string.Format(CultureInfo.InvariantCulture, " ({0},{1})", value.Line, value.Position));
        }

        public XElement CreateRow(string key, FullSymbol value)
        {
            return value.IsValid ? CreateRow(key, value.ToString(m_symbolTable)) : null;
        }

        public XElement CreateRow(string key, PipId value)
        {
            return value.IsValid ? CreateRow(key, value.IsValid ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty) : null;
        }

        public XElement CreateRow(string key, IEnumerable<PipId> values)
        {
            var allPips = values
                .Select(pipId => m_pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer))
                .Select(
                    pip => new
                           {
                               PipType = pip.PipType.ToString(),
                               Hash = string.Format("{0}{1:X16}", Pip.SemiStableHashPrefix, pip.Provenance?.SemiStableHash),
                               FullName = pip.Provenance?.OutputValueSymbol.ToString(m_symbolTable),
                               Spec = pip.Provenance?.Token.Path.ToString(m_pathTable),
                               Details = GetPipDetails(pip),
                           })
                .OrderBy(obj => obj.PipType)
                .ThenBy(obj => obj.FullName)
                .Select(obj => string.Format("[{0}] <{1}> {2} - {3} {4}", obj.Hash, obj.PipType, obj.FullName, obj.Spec, obj.Details));

            return CreateRow(key, allPips);
        }

        private string GetPipDetails(Pip pip)
        {
            switch (pip.PipType)
            {
                case PipType.SealDirectory:
                    var sealedDir = (SealDirectory)pip;
                    return $"|| [{sealedDir.Contents.Length} files] - {sealedDir.Directory.Path.ToString(m_pathTable)} - {Enum.Format(typeof(SealDirectoryKind), sealedDir.Kind, "f")} - [Scrub for Full seal: {sealedDir.Scrub}]";
                case PipType.HashSourceFile:
                    var hashFile = (HashSourceFile)pip;
                    return $"|| {hashFile.Artifact.Path.ToString(m_pathTable)}";
                case PipType.Value:
                    var value = (ValuePip)pip;
                    return $"|| {value.Symbol.ToString(m_symbolTable)}";
                default:
                    return string.Empty;
            }
        }
    }
}
