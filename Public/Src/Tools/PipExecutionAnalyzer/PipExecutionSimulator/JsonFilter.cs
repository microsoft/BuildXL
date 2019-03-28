// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PipExecutionSimulator
{
    public class JsonFilter : JsonReader
    {
        private readonly StreamReader m_streamReader;
        private readonly JsonReader m_reader;
        private readonly JsonTextReader m_otherReader;
        private readonly JsonTextWriter m_writer;
        private int m_lineNumber;
        private string m_propertyName;

        public JsonFilter(string path, JsonReader reader, JsonTextWriter writer)
        {
            m_reader = reader;
            m_streamReader = new StreamReader(path);
            m_otherReader = new JsonTextReader(m_streamReader);
            m_writer = writer;
        }

        public override JsonToken TokenType
        {
            get
            {
                return m_reader.TokenType;
            }
        }

        public override void Close()
        {
            m_streamReader.Close();
            m_reader.Close();
            m_writer.Close();
        }

        public override int Depth
        {
            get
            {
                return m_reader.Depth;
            }
        }

        public override string Path
        {
            get
            {
                return m_reader.Path;
            }
        }

        public override char QuoteChar
        {
            get
            {
                return m_reader.QuoteChar;
            }
        }

        public override object Value
        {
            get
            {
                return m_reader.Value;
            }
        }

        public override Type ValueType
        {
            get
            {
                return m_reader.ValueType;
            }
        }

        public override bool Read()
        {
            if (m_reader.Read())
            {
                ReadOtherReader();
                switch (m_otherReader.TokenType)
                {
                    case JsonToken.Boolean:
                    case JsonToken.Bytes:
                    case JsonToken.Date:
                    case JsonToken.String:
                    case JsonToken.Integer:
                    case JsonToken.Float:
                        m_writer.WriteValue(m_otherReader.Value);
                        break;
                    case JsonToken.Comment:
                        m_writer.WriteComment((string)m_otherReader.Value);
                        break;
                    case JsonToken.EndArray:
                        m_writer.WriteEndArray();
                        break;
                    case JsonToken.EndConstructor:
                        m_writer.WriteEndConstructor();
                        break;
                    case JsonToken.EndObject:
                        m_writer.WriteEndObject();
                        break;
                    case JsonToken.Null:
                        m_writer.WriteNull();
                        break;
                    case JsonToken.Raw:
                        break;
                    case JsonToken.StartArray:
                        m_writer.WriteStartArray();
                        break;
                    case JsonToken.StartConstructor:
                        m_writer.WriteStartConstructor((string)m_otherReader.Value);
                        break;
                    case JsonToken.StartObject:
                        m_writer.WriteStartObject();
                        break;
                    case JsonToken.PropertyName:
                        // Property names will be handled when value is written
                        break;
                    default:
                        Contract.Assert(false, "unhandled token type");
                        break;
                }

                return true;
            }

            return false;
        }

        private void ReadOtherReader()
        {
            if (m_otherReader.TokenType == JsonToken.PropertyName)
            {
                m_writer.WritePropertyName((string)m_otherReader.Value);
            }

            if (m_otherReader.LineNumber != m_lineNumber)
            {
                m_writer.WriteWhitespace(Environment.NewLine);
                m_lineNumber = m_otherReader.LineNumber;
                m_writer.WriteWhitespace(string.Empty.PadLeft(m_otherReader.LinePosition, ' '));
            }

            m_otherReader.Read();
        }

        public override byte[] ReadAsBytes()
        {
            return ReadAndWriteResult(m_reader.ReadAsBytes());
        }

        public override DateTime? ReadAsDateTime()
        {
            return ReadAndWriteResult(m_reader.ReadAsDateTime());
        }

        public override DateTimeOffset? ReadAsDateTimeOffset()
        {
            return ReadAndWriteResult(m_reader.ReadAsDateTimeOffset());
        }

        public override decimal? ReadAsDecimal()
        {
            return ReadAndWriteResult(m_reader.ReadAsDecimal());
        }

        public override int? ReadAsInt32()
        {
            return ReadAndWriteResult(m_reader.ReadAsInt32());
        }

        public override string ReadAsString()
        {
            return ReadAndWriteResult(m_reader.ReadAsString());
        }

        public new void Skip()
        {
            //if (m_otherReader.TokenType == JsonToken.PropertyName)
            //{
            //    m_writer.WriteValue("");
            //}

            m_reader.Skip();
            m_otherReader.Skip();
        }

        private T ReadAndWriteResult<T>(T result)
        {
            ReadOtherReader();
            m_writer.WriteToken(m_otherReader);
            return result;
        }
    }
}
