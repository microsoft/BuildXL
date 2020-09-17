using System;

using Helloworld;

namespace CopyClient
{
    public class CopyResult
    {
        public string FileName;

        public CopyResultStatus Status;

        public string ErrorType;

        public string ErrorMessage;

        public CopyCompression Compression;

        // The time waiting for a connection from the target machine.
        public TimeSpan ResponseTime;

        // The time spent streaming bytes after a connection is established.
        public TimeSpan StreamingTime;

        // The number of bytes streamed, which, due to compression,
        // may not equal the file size, even for successful copies.
        public long BytesStreamed;

        public long ContentSize;

        public override string ToString()
        {
            return $"FileName={FileName};Status={Status};ErrorType={ErrorType};Compression={Compression};ResponseTime={ResponseTime};StreamingTime={StreamingTime};BytesStreamed={BytesStreamed};ContentSize={ContentSize};";
        }
    }

    public enum CopyResultStatus
    {
        /// <summary>
        /// The copy was successfully completed.
        /// </summary>
        Successful = 0,

        /// <summary>
        /// An error occured when the client tried to connect to the server.
        /// </summary>
        ConnectionFailure = 1,

        /// <summary>
        /// The allowed time for the client to connect to the server was exceeded.
        /// </summary>
        ConnectionTimeout = 2,

        /// <summary>
        /// The server notified the client that too many copies are ongoing.
        /// </summary>
        RequestThrottled = 3,

        /// <summary>
        /// The requested file was not found.
        /// </summary>
        FileNotFound = 4,

        /// <summary>
        /// The supplied file was already present.
        /// </summary>
        FileAlreadyPresent = 5,

        /// <summary>
        /// A file I/O error occured on the server.
        /// </summary>
        FileAccessErrorOnServer = 6,

        /// <summary>
        /// A file I/O error occured on the client.
        /// </summary>
        FileAccessErrorOnClient = 7,

        /// <summary>
        /// An error occured while transfering the content.
        /// </summary>
        StreamingFailure = 8,

        /// <summary>
        /// The allowed time for the content to be transfered was exceeded.
        /// </summary>
        StreamingTimeout = 9,

        /// <summary>
        /// The copy operation was cancelled.
        /// </summary>
        OperationCanceled = 10,

        /// <summary>
        /// An unclassified error occured on the server.
        /// </summary>
        OtherServerSideError = 11,

        /// <summary>
        /// An unclassified error occured on the client.
        /// </summary>
        OtherClientSideError = 12,

        /// <summary>
        /// The transfer was completed but the resulting content failed a validation check.
        /// </summary>
        InvalidHash = 13
    }

}
