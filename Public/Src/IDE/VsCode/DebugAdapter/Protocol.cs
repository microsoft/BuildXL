// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using VSCode.DebugProtocol;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!
#pragma warning disable SA1649 // File name must match first type name

namespace VSCode.DebugAdapter
{
    /**
     * The ProtocolServer can be used to implement a server that uses the VSCode debug protocol.
     */
    public abstract class ProtocolServer
    {
        protected const int BufferSize = 4096;
        protected const string TwoCRLF = "\r\n\r\n";

        protected static readonly Regex ContentLengthRegex = new Regex(@"Content-Length: (\d+)");
        protected static readonly Encoding Encoding = Encoding.UTF8;

        private static readonly JsonSerializerSettings s_jsonSettings = CreateSerializerSettings();

        private readonly ByteBuffer m_rawData;
        private Stream m_outputStream;
        private int m_bodyLength;
        private int m_sequenceNumber;
        private bool m_stopRequested;

        private static JsonSerializerSettings CreateSerializerSettings()
        {
            var ans = new JsonSerializerSettings();
            ans.ContractResolver = new CamelCasePropertyNamesContractResolver();
            return ans;
        }

        protected ProtocolServer()
        {
            m_sequenceNumber = 1;
            m_bodyLength = -1;
            m_rawData = new ByteBuffer();
        }

        public async Task StartAsync(Stream inputStream, Stream outputStream)
        {
            m_outputStream = outputStream;
            byte[] buffer = new byte[BufferSize];
            m_stopRequested = false;
            while (!m_stopRequested)
            {
                var read = await inputStream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    // end of stream
                    break;
                }

                if (read > 0)
                {
                    m_rawData.Append(buffer, read);
                    ProcessData();
                }
            }
        }

        public void Stop()
        {
            m_stopRequested = true;
        }

        public virtual void SendEvent<T>(IEvent<T> e)
        {
            SendMessage(e);
        }

        protected abstract void DispatchRequest(IRequest request);

        // ---- private ------------------------------------------------------------------------
        private void ProcessData()
        {
            while (true)
            {
                if (m_bodyLength >= 0)
                {
                    if (m_rawData.Length >= m_bodyLength)
                    {
                        var buf = m_rawData.RemoveFirst(m_bodyLength);
                        m_bodyLength = -1;
                        Dispatch(Encoding.GetString(buf));
                        continue;   // there may be more complete messages to process
                    }
                }
                else
                {
                    string s = m_rawData.GetString(Encoding);
                    var idx = s.IndexOf(TwoCRLF, StringComparison.Ordinal);
                    if (idx != -1)
                    {
                        Match m = ContentLengthRegex.Match(s);
                        if (m.Success && m.Groups.Count == 2)
                        {
                            m_bodyLength = Convert.ToInt32(m.Groups[1].ToString(), CultureInfo.InvariantCulture);
                            m_rawData.RemoveFirst(idx + TwoCRLF.Length);
                            continue;   // try to handle a complete message
                        }
                    }
                }

                break;
            }
        }

        protected virtual void Dispatch(string req)
        {
            var request = JsonConvert.DeserializeObject<Request>(req);
            if (request != null && request.Type == "request")
            {
                DispatchRequest(request);
            }
        }

        protected virtual void SendMessage(IProtocolMessage message)
        {
            message.Seq = m_sequenceNumber++;
            var data = ConvertToBytes(message);
            try
            {
                m_outputStream.Write(data, 0, data.Length);
                m_outputStream.Flush();
            }
#pragma warning disable ERP022 // TODO: This should really catch the right exceptions
            catch (Exception)
            {
            }
#pragma warning restore ERP022
        }

        private static byte[] ConvertToBytes(IProtocolMessage request)
        {
            var asJson = JsonConvert.SerializeObject(request, s_jsonSettings);
            byte[] jsonBytes = Encoding.GetBytes(asJson);

            string header = string.Format(CultureInfo.InvariantCulture, "Content-Length: {0}{1}", jsonBytes.Length, TwoCRLF);
            byte[] headerBytes = Encoding.GetBytes(header);

            byte[] data = new byte[headerBytes.Length + jsonBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, data, 0, headerBytes.Length);
            Buffer.BlockCopy(jsonBytes, 0, data, headerBytes.Length, jsonBytes.Length);

            return data;
        }
    }

    //--------------------------------------------------------------------------------------
    internal sealed class ByteBuffer
    {
        private byte[] m_buffer;

        public ByteBuffer()
        {
            m_buffer = new byte[0];
        }

        public int Length
        {
            get { return m_buffer.Length; }
        }

        public string GetString(Encoding enc)
        {
            return enc.GetString(m_buffer);
        }

        public void Append(byte[] b, int length)
        {
            byte[] newBuffer = new byte[m_buffer.Length + length];
            Buffer.BlockCopy(m_buffer, 0, newBuffer, 0, m_buffer.Length);
            Buffer.BlockCopy(b, 0, newBuffer, m_buffer.Length, length);
            m_buffer = newBuffer;
        }

        public byte[] RemoveFirst(int n)
        {
            byte[] b = new byte[n];
            Buffer.BlockCopy(m_buffer, 0, b, 0, n);
            byte[] newBuffer = new byte[m_buffer.Length - n];
            Buffer.BlockCopy(m_buffer, n, newBuffer, 0, m_buffer.Length - n);
            m_buffer = newBuffer;
            return b;
        }
    }
}
