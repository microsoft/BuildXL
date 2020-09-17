This solution is intended as a demonstration test-bed for streaming
files over grpc.

The projects are:
 * ProtoGen defines the client/server contract. The NuGet packages
   it depends on cause shared types to be auto-generated
   based on the .proto file on compilation.
 * CopyShared defines types shared between the client and server.
   These are primarily types representing information passed in headers.
 * CopyServer defines a service implementation that
   streams the bytes of any file it is asked for.
 * CopyClient sends requests to CopyServer and puts
   the bytes it receives into its own working directory.

The basic scenario is to start CopyServer, then start
CopyClient and give it paths to files you want copied. The
server reports requests to it and the client reports
telemetry for each copy.

By starting CopyServer with different arguments, you can
specify various server-side behaviors to trigger different
client-side results, e.g.:
  Normal, pull existing file -> Success
  Normal, pull non-existing file -> FileNotFound
  SlowResponse -> ConnectionTimeout
  ThrowResponse -> ConnectionFailure
  ThrowOpening -> FileAccessErrorOnServer
  SlowStreaming -> StreamingTimeout
  ThrowStreaming -> StreamingFailure
  Do not start CopyServer -> ConnectionFailure
In addition to the result status, each operation also returns
the time spent connecting, the time spent streaming, the
and the total number of bytes and chunks transmitted.

One aspect of this system which required significant work
was support for gzip compressed transmission. What made
this particularly hard was that GZipStream expects to write/read
to/from streams on compression/decompression, but grpc expects
implementors to write/read chunks on server/client side. This
required the introdution of BufferedWriteStream and
BufferedReadStream to convert between streams and chunks.
Compression reduces transmission times x3-x10 for large files.