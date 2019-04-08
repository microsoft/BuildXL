using System;
using Grpc.Core;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Helloworld;

namespace CopyClient
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        private static MemoryStream Decompress (string file)
        {
            using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using (GZipStream unzipper = new GZipStream(stream, CompressionMode.Decompress, true))
                {
                    MemoryStream output = new MemoryStream();
                    unzipper.CopyTo(output);
                    return output;
                }
            }
        }

        private static void Compress (MemoryStream input, string file)
        {
            using (FileStream stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using (GZipStream zipper = new GZipStream(stream, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    input.CopyTo(zipper);
                    zipper.Flush();
                }
            }
        }

        static async Task MainAsync (string[] args)
        {
            Channel channel = new Channel($"localhost:{CopyConstants.PortNumber}", ChannelCredentials.Insecure);
            CopyClient client = new CopyClient(channel);

            while (true)
            {
                Console.WriteLine("Enter name:");
                string name = Console.ReadLine();

                await client.Copy(name);              
            }
        }
    }
}
