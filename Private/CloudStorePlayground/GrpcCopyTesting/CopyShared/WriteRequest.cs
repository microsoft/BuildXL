using System;

using Grpc.Core;

namespace Helloworld
{
    public class WriteRequest
    {
        public CopyCompression Compression;

        public string FileName;

        public Metadata ToMetadata ()
        {
            Metadata headers = new Metadata();
            headers.Add("compression", Compression.ToString());
            headers.Add("filename", FileName);
            return (headers);
        }

        public static WriteRequest FromMetadata (Metadata headers)
        {
            WriteRequest request = new WriteRequest();
            foreach (var header in headers)
            {
                switch (header.Key)
                {
                    case "compression": request.Compression = Enum.Parse<CopyCompression>(header.Value); break;
                    case "filename": request.FileName = header.Value; break;
                }
            }
            return request;
        }
    }
}
