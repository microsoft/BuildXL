// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Text;
using static BuildXL.Utilities.FormattableStringEx;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Diagnostics
{
    /// <summary>
    /// Diagnostic information from the compiler.
    /// </summary>
    public interface IDiagnosticMessage
    {
        /// <nodoc />
        string Key { get; }

        /// <nodoc />
        DiagnosticCategory Category { get; }

        /// <nodoc />
        int Code { get; }

        /// <nodoc />
        string Message { get; }
    }

    /// <nodoc/>
    [DebuggerDisplay("{ToString(), nq}")]
    public sealed class DiagnosticMessage : IDiagnosticMessage
    {
        /// <nodoc />
        public string Key { get; set; }

        /// <nodoc />
        public DiagnosticCategory Category { get; set; }

        /// <nodoc />
        public int Code { get; set; }

        /// <nodoc />
        public string Message { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"{Key}:{Category} {Code}: {Message}");
        }
    }

    /// <summary>
    /// A linked list of formatted diagnostic messages to be used as part of a multiline message.
    /// It is built from the bottom up, leaving the head to be the "main" diagnostic.
    /// While it seems that DiagnosticMessageChain is structurally similar to DiagnosticMessage,
    /// the difference is that messages are all preformatted in DMC.
    /// </summary>
    public sealed class DiagnosticMessageChain
    {
        /// <nodoc />
        public DiagnosticMessageChain(string messageText, DiagnosticCategory category, int code, DiagnosticMessageChain next)
        {
            MessageText = messageText;
            Category = category;
            Code = code;
            Next = next;
        }

        /// <nodoc />
        public string MessageText { get; }

        /// <nodoc />
        public DiagnosticCategory Category { get; }

        /// <nodoc />
        public int Code { get; }

        /// <nodoc />
        public DiagnosticMessageChain Next { get; internal set; }
    }

    /// <summary>
    /// Union type for string and <see cref="DiagnosticMessageChain"/>.
    /// </summary>
    [DebuggerDisplay("{ToString(), nq}")]
    public sealed class Message
    {
        private readonly string m_message;
        private readonly DiagnosticMessageChain m_messageChain;

        private Message(string message)
        {
            Contract.Requires(message != null);
            m_message = message;
        }

        private Message(DiagnosticMessageChain messageChain)
        {
            Contract.Requires(messageChain != null);
            m_messageChain = messageChain;
        }

        /// <nodoc />
        public static implicit operator Message(string message)
        {
            return new Message(message);
        }

        /// <nodoc />
        public static implicit operator Message(DiagnosticMessageChain chain)
        {
            return chain == null ? null : new Message(chain);
        }

        /// <nodoc />
        public static explicit operator string(Message message)
        {
            Contract.Assert(message.m_message != null);
            return message.m_message;
        }

        /// <nodoc />
        public static explicit operator DiagnosticMessageChain(Message message)
        {
            Contract.Assert(message.m_messageChain != null);
            return message.m_messageChain;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (m_message != null)
            {
                return m_message;
            }

            // see flattenDiagnosticMessageText from program.ts to see similar implementation
            DiagnosticMessageChain chain = m_messageChain;
            int indent = 0;
            var builder = new StringBuilder();

            while (chain != null)
            {
                if (indent != 0)
                {
                    builder.AppendLine();
                    builder.Append(new string(' ', indent * 2));
                }

                builder.Append(chain.MessageText);
                indent++;
                chain = chain.Next;
            }

            return builder.ToString();
        }

        /// <nodoc />
        public string AsString()
        {
            return m_message;
        }

        /// <nodoc />
        public DiagnosticMessageChain AsDiagnosticMessageChain()
        {
            return m_messageChain;
        }
    }
}
