using System;
using System.Collections.Generic;
using System.Text;

using Grpc.Core;

namespace Helloworld
{

    // The response to a read request that is sent via headers.

    public class ReadResponse
    {
        public string FileName;

        public CopyCompression Compression;

        public long Offset;

        public long FileSize = -1L;

        public int ChunkSize = -1;

        public string ErrorType;

        public string ErrorMessage;

        public Metadata ToHeaders ()
        {
            Metadata headers = new Metadata();
            headers.Add("filename", FileName);
            headers.Add("compression", Compression.ToString());
            headers.Add("offset", Offset.ToString());
            if (FileSize > 0) headers.Add("filesize", FileSize.ToString());
            if (ChunkSize > 0) headers.Add("chunksize", ChunkSize.ToString());
            if (ErrorType != null) headers.Add("errortype", ErrorType);
            if (ErrorMessage != null) headers.Add("errormessage", ErrorMessage);
            return (headers);
        }

        public static ReadResponse FromHeaders (Metadata headers)
        {
            ReadResponse response = new ReadResponse();
            foreach (var header in headers)
            {
                switch (header.Key)
                {
                    case "filename": response.FileName = header.Value; break;
                    case "compression": response.Compression = Enum.Parse<CopyCompression>(header.Value); break;
                    case "offset": response.Offset = Int64.Parse(header.Value); break;
                    case "filesize": response.FileSize = Int64.Parse(header.Value); break;
                    case "chunksize": response.ChunkSize = Int32.Parse(header.Value); break;
                    case "errortype": response.ErrorType = header.Value; break;
                    case "errormesage": response.ErrorMessage = header.Value; break;
                }
            }
            return response;
        }

    }
}
