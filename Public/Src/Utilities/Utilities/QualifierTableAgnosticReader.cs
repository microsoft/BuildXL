// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A reader that deserializes qualifier objects in a table-agnostic way (e.g. as strings)
    /// </summary>
    /// <remarks>
    /// Useful for deserializing objects that may come from different qualifier tables. Should be used in correspondance with <see cref="QualifierTableAgnosticWriter"/>.
    /// /// TODO: This class could be removed when we start serializing the qualifier table
    /// </remarks>
    public class QualifierTableAgnosticReader : BuildXLReader
    {
        private readonly QualifierTable m_qualifierTable;

        /// <nodoc/>
        public QualifierTableAgnosticReader(QualifierTable qualifierTable, bool debug, Stream stream, bool leaveOpen)
            : base(debug, stream, leaveOpen)
        {
            m_qualifierTable = qualifierTable;
        }

        /// <summary>
        /// Reads a QualifierSpaceId from a string representation
        /// </summary>
        public override QualifierSpaceId ReadQualifierSpaceId()
        {
            Start<QualifierSpaceId>();
            var isValid = ReadBoolean();
            if (!isValid)
            {
                return QualifierSpaceId.Invalid;
            }

            // The qualifier space is stored as <key, value[]>[]
            var qualifierSpace = ReadArray(reader => new Tuple<string, string[]>(reader.ReadString(), ReadArray(reader2 => reader2.ReadString())));

            var value = m_qualifierTable.CreateQualifierSpace(qualifierSpace);
            End();
            return value;
        }

        /// <summary>
        /// Reads a QualifierId from a string representation
        /// </summary>
        public override QualifierId ReadQualifierId()
        {
            Start<QualifierId>();
            var isValid = ReadBoolean();
            if (!isValid)
            {
                return QualifierId.Invalid;
            }

            var keyValues = ReadArray(reader => new Tuple<string, string>(reader.ReadString(), reader.ReadString()));

            var result = m_qualifierTable.CreateQualifier(keyValues);

            End();
            return result;
        }
    }
}
