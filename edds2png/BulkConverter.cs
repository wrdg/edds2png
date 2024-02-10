using K4os.Compression.LZ4.Encoders;
using Pfim;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace edds2png
{
    internal class BulkConverter
    {
        private readonly List<string> _imagePaths;

        public BulkConverter()
        {
            _imagePaths = new List<string>();
        }

        public void Add(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!path.EndsWith(".edds", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _imagePaths.Add(path);
        }

        public void Process()
        {
            List<Exception> conversionExceptions = new List<Exception>();
            foreach (var imagePath in _imagePaths.AsEnumerable())
            {
                try
                {
                    string fullPath = Path.GetFullPath(imagePath);
                    Convert(fullPath);

                    Console.WriteLine($"{fullPath} -> {Path.ChangeExtension(fullPath, ".png")}");
                }
                catch (Exception inner)
                {
                    conversionExceptions.Add(inner);
                }
            }

            if (conversionExceptions.Count != 0)
            {
                throw new AggregateException(conversionExceptions);
            }
        }

        public bool CanProcess()
        {
            return _imagePaths.Count != 0;
        }

        private static unsafe void Convert(string imagePath)
        {
            using (var stream = DecompressEDDS(imagePath))
            {
                using (var image = Pfimage.FromStream(stream))
                {
                    PixelFormat format;
                    switch (image.Format)
                    {
                        case Pfim.ImageFormat.Rgba32:
                            {
                                format = PixelFormat.Format32bppArgb;
                                break;
                            }
                        default:
                            {
                                throw new NotImplementedException();
                            }
                    }


                    var handle = GCHandle.Alloc(image.Data, GCHandleType.Pinned);
                    try
                    {
                        var data = Marshal.UnsafeAddrOfPinnedArrayElement(image.Data, 0);
                        var bitmap = new Bitmap(image.Width, image.Height, image.Stride, format, data);
                        bitmap.Save(Path.ChangeExtension(imagePath, ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    finally
                    {
                        handle.Free();
                    }
                }
            }
        }

        private static MemoryStream DecompressEDDS(string imagePath)
        {
            List<int> copyBlocks = new List<int>();
            List<int> lz4Blocks = new List<int>();
            List<byte> decodedBlocks = new List<byte>();

            void FindBlocks(BinaryReader reader)
            {
                while (true)
                {
                    byte[] blocks = reader.ReadBytes(4);
                    char[] dd = Encoding.UTF8.GetChars(blocks);

                    string block = new string(dd);
                    int size = reader.ReadInt32();

                    switch (block)
                    {
                        case "COPY": copyBlocks.Add(size); break;
                        case "LZ4 ": lz4Blocks.Add(size); break;
                        default: reader.BaseStream.Seek(-8, SeekOrigin.Current); return;
                    }
                }
            }

            using (var reader = new BinaryReader(File.Open(imagePath, FileMode.Open)))
            {
                byte[] dds_header = reader.ReadBytes(128);
                byte[] dds_header_dx10 = null;

                if (dds_header[84] == 'D' && dds_header[85] == 'X' && dds_header[86] == '1' && dds_header[87] == '0')
                {
                    dds_header_dx10 = reader.ReadBytes(20);
                }

                FindBlocks(reader);

                foreach (int count in copyBlocks)
                {
                    byte[] buff = reader.ReadBytes(count);
                    decodedBlocks.InsertRange(0, buff);
                }

                foreach (int Length in lz4Blocks)
                {
                    LZ4ChainDecoder lz4ChainDecoder = new LZ4ChainDecoder(65536, 0);
                    uint size = reader.ReadUInt32();
                    byte[] target = new byte[size];

                    int num = 0;
                    int count1 = 0;
                    int idx = 0;
                    for (; num < Length - 4; num += count1 + 4)
                    {
                        count1 = reader.ReadInt32() & int.MaxValue;
                        byte[] numArray = reader.ReadBytes(count1);
                        byte[] buffer = new byte[65536];
                        LZ4EncoderExtensions.DecodeAndDrain(lz4ChainDecoder, numArray, 0, count1, buffer, 0, 65536, out int count2);

                        Array.Copy(buffer, 0, target, idx, count2);
                        idx += count2;
                    }

                    decodedBlocks.InsertRange(0, target);
                }

                if (dds_header_dx10 != null)
                {
                    decodedBlocks.InsertRange(0, dds_header_dx10);
                }

                decodedBlocks.InsertRange(0, dds_header);
                byte[] final = decodedBlocks.ToArray();

                MemoryStream stream = new MemoryStream();
                stream.Write(final, 0, final.Length);
                stream.Position = 0;

                return stream;
            }
        }
    }
}
