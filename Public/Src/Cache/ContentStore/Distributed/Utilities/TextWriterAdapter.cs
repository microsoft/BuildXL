using System;
using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    internal class TextWriterAdapter : TextWriter
    {
        private readonly Context _context;
        private readonly Severity _severity;

        public TextWriterAdapter(Context context, Severity severity, IFormatProvider? formatProvider = null) : base(formatProvider)
        {
            Contract.Assert(severity != Severity.Always);

            _context = context;
            _severity = severity;
        }

        public override Encoding Encoding => Encoding.Default;

        public override void Write(char value)
        {
            // Empty on purpose. We don't support writing out single chars.
        }

        public override void WriteLine()
        {
            // Empty on purpose. TextWriter will use this to add a newline at the end.
        }

        public override void Write(char[] buffer, int index, int count)
        {
            WriteLine(new string(buffer, index, count));
        }

        public override void Write(string? value)
        {
            WriteLine(value);
        }

        public override void WriteLine(string? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            _context.TraceMessage(_severity, value);
        }
    }
}
