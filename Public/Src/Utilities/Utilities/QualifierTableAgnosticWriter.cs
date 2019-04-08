// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A writer that serializes qualifier-related objects in a table-agnostic way
    /// </summary>
    /// <remarks>
    /// Useful for serializing objects that may come from different qualifier tables. Should be used in correspondance with <see cref="QualifierTableAgnosticReader"/>.
    /// TODO: This class could be removed when we start serializing the qualifier table
    /// </remarks>
    public class QualifierTableAgnosticWriter : BuildXLWriter
    {
        private readonly QualifierTable m_qualifierTable;
        private readonly StringTable m_stringTable;

        /// <nodoc/>
        public QualifierTableAgnosticWriter(QualifierTable qualifierTable, bool debug, Stream stream, bool leaveOpen, bool logStats)
            : base(debug, stream, leaveOpen, logStats)
        {
            m_qualifierTable = qualifierTable;
            m_stringTable = qualifierTable.StringTable;
        }

        /// <summary>
        /// Writes a QualifierSpaceId using its underlying string representation
        /// </summary>
        public override void Write(QualifierSpaceId qualifierSpaceId)
        {
            Start<QualifierId>();
            Write(qualifierSpaceId.IsValid);

            if (qualifierSpaceId.IsValid)
            {
                var qualifierSpace = m_qualifierTable.GetQualifierSpace(qualifierSpaceId);

                var keys = qualifierSpace.Keys;
                var values = qualifierSpace.Values;
                var defaults = qualifierSpace.Defaults;

                // The qualifier space is stored as a <key, value[]>[]

                // Get the string representation based on the string table
                // The values of a given key are returned sorted, which means that, for V1, the default value (the first value in the list) might be off
                // So we retrieve the default as well and make sure it's the first one.
                var stringRepresentation = new Tuple<string, string[]>[keys.Count];
                for (var i = 0; i < keys.Count; i++)
                {
                    stringRepresentation[i] = new Tuple<string, string[]>(
                        keys[i].ToString(m_stringTable),

                        // We put the default first. This is for V1 compatibility.
                        new List<string> { defaults[i].ToString(m_stringTable) }.Concat(
                            values[i].Where(value => value != defaults[i]).Select(value => value.ToString(m_stringTable))).ToArray());
                }

                Write(
                    stringRepresentation,
                    (writer, tuple) =>
                    {
                        writer.Write(tuple.Item1);
                        writer.Write(tuple.Item2, (writer2, value) => writer2.Write(value));
                    });
            }

            End();
        }

        /// <summary>
        /// Writes a QualifierId using its underlying string representation
        /// </summary>
        public override void Write(QualifierId qualifierId)
        {
            Start<QualifierId>();
            Write(qualifierId.IsValid);

            if (qualifierId.IsValid)
            {
                var qualifier = m_qualifierTable.GetQualifier(qualifierId);

                // the qualifier is stored as <key, value>[]
                var keyValues = new Tuple<string, string>[qualifier.Keys.Count];
                for (var i = 0; i < keyValues.Length; i++)
                {
                    keyValues[i] = new Tuple<string, string>(qualifier.Keys[i].ToString(m_stringTable), qualifier.Values[i].ToString(m_stringTable));
                }

                Write(
                    ReadOnlyArray<Tuple<string, string>>.FromWithoutCopy(keyValues),
                    (writer, value) =>
                    {
                        writer.Write(value.Item1);
                        writer.Write(value.Item2);
                    });
            }

            End();
        }
    }
}
