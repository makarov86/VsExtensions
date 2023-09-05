using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace ReBitmap
{
    public static class ImgTest
    {
        public static readonly Guid Marker1 = Guid.Parse("0c3cd8ec-eb14-4dc4-aab2-4ddd40ae0c5e");
        public static readonly Guid Marker2 = Guid.Parse("922b0bc5-6733-45c4-b3f4-bfd9832e98d3");

        public const int MaxWidth = 1850;
        public const int MaxHeight = 850;

        public static readonly int HeaderSize = Marshal.SizeOf<Header>();
        public static readonly int HeaderSizePack = (int) (HeaderSize * 8.0 / 6.0);
        public static readonly int MaxFileSize = (int) (MaxHeight * (MaxWidth / 4.0) * 6.0 / 8.0 - HeaderSize - 1); //
        public static readonly int MinFileSize = (int) (Math.Pow(HeaderSizePack + 1, 2) + 1);

        [STAThread]
        static void Main(string[] args)
        {
            var input = @"C:\Temp\INPUT.rar";

            if (!File.Exists(input))
            {
                MessageBox.Show($"{input} not exists!");
                return;
            }

            var file = new FileInfo(input);

            if (file.Length > MaxFileSize)
            {
                MessageBox.Show($"Max size is {Math.Round(1.0 * MaxFileSize / (1024 * 1024), 2)} Mb");
                return;
            }

            if (file.Length < MinFileSize)
            {
                MessageBox.Show($"File is empty or very small! Min size is {Math.Round(1.0 * MinFileSize / 1024, 2)} Kb");
                return;
            }

            var fileNameBytes = Encoding.UTF8.GetBytes(file.Name);

            if (fileNameBytes.Length > 32)
            {
                MessageBox.Show($"File name is too long!");
                return;
            }

            if (fileNameBytes.Length < 32)
            {
                fileNameBytes = fileNameBytes.Concat(new byte[32 - fileNameBytes.Length]).ToArray();
            }

            var header = new Header();
            var data = File.ReadAllBytes(input);

            using (var md5 = MD5.Create())
            {
                using (var stream = new MemoryStream(data))
                {
                    header.Hash = md5.ComputeHash(stream);
                }
            }

            if (header.Hash.Length != 16)
            {
                throw new InvalidOperationException();
            }

            byte[] Pack(byte[] inputBytes)
            {
                using (var packStream = new MemoryStream())
                {
                    using (var pack = new SixBitsStream(packStream, SixBitsStream.Mode.Pack))
                    {
                        pack.Write(inputBytes, 0, inputBytes.Length);
                    }

                    return packStream.ToArray();
                }
            }

            header.FileOriginalLenght = data.Length;

            var totalSize = (int)(data.Length * 8.0 / 6.0) + HeaderSizePack;
            const int DuplCoeef = 4;

            (header.Width, header.Height) = GetOptimalSize(MaxWidth, MaxHeight, totalSize * DuplCoeef); //totalSize multipy duplCoeef
            header.Width += header.Width % DuplCoeef;
            header.Marker = Marker1.ToByteArray().Concat(Marker2.ToByteArray()).ToArray();
            header.Name = fileNameBytes;

            var headerBytes = new byte[HeaderSize];
            var pointer = Marshal.AllocHGlobal(HeaderSize);
            Marshal.StructureToPtr(header, pointer, true);
            Marshal.Copy(pointer, headerBytes, 0, HeaderSize);
            Marshal.FreeHGlobal(pointer);

            using (var bitmap = new Bitmap(header.Width, header.Height, PixelFormat.Format24bppRgb))
            {
                int x = 0, y = 0;

                foreach (var color in ColorsFromBytes(Pack(headerBytes.Concat(data).ToArray())))
                {
                    for (var i = 0; i < DuplCoeef; i++) //Duplicate info
                    {
                        bitmap.SetPixel(x++, y, color);
                    }

                    if (x == header.Width)
                    {
                        y++;
                        x = 0;
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1(bitmap));
            }
        }

        private static int Norm(int val)
        {
            if (val == 0b01000000)
            {
                return 85;
            }

            if (val == 0b10000000)
            {
                return 170;
            }

            if (val == 0b11000000)
            {
                return 255;
            }

            return val;
        }

        private static IEnumerable<Color> ColorsFromBytes(byte[] bytes)
        {
            foreach (var b in bytes)
            {
                int r = (b & 0b00110000) << 2;
                int g = (b & 0b00001100) << 4;
                int bb = (b & 0b00000011) << 6;

                yield return Color.FromArgb(Norm(r), Norm(g), Norm(bb));
            }
        }

        private static (int, int) GetOptimalSize(int maxWidth, int maxHeight, long totalSize)
        {
            var minSide = Math.Min(maxWidth, maxHeight);

            var square = Convert.ToInt32(Math.Floor(Math.Sqrt(totalSize)) + 1);

            if (square <= minSide)
            {
                return (square, square);
            }

            var secondSide = (int)(totalSize + (minSide - (totalSize % minSide))) / minSide;

            if (maxHeight == minSide)
            {
                return (secondSide, minSide);
            }

            return (minSide, secondSide);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Header
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] Marker;
            public int Width;
            public int Height;
            public long FileOriginalLenght;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] Name;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] Hash;
        }

        public class SixBitsStream : Stream
        {
            private readonly Stream _outerStream;
            private readonly Mode _mode;

            private int _previosOutputByteFreeBits;
            private int _previosInputByteRemBits;

            public SixBitsStream(Stream stream, Mode mode)
            {
                _outerStream = stream ?? throw new ArgumentNullException(nameof(stream));
                _mode = mode;
                _previosOutputByteFreeBits = 0;
                _previosInputByteRemBits = 0;
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override int Read(byte[] bufferForUnPack, int offset, int count)
            {
                if (_mode == Mode.Pack)
                {
                    throw new NotSupportedException();
                }

                if (count < 0 || offset < 0)
                {
                    throw new ArgumentOutOfRangeException();
                }

                if (offset + count > bufferForUnPack.Length)
                {
                    throw new ArgumentOutOfRangeException();
                }

                if (!_outerStream.CanRead)
                {
                    throw new InvalidOperationException();
                }

                if (bufferForUnPack.Length == 0 || count == 0 || offset == bufferForUnPack.Length)
                {
                    return 0;
                }

                int inputByte;
                int resultIndex = offset;
                int needBitCountForByte = 0;

                while (resultIndex < count && (inputByte = _outerStream.ReadByte()) != -1)
                {
                    int previosByte;
                    switch ($"{needBitCountForByte}_{_previosInputByteRemBits}")
                    {
                        case "0_0":
                            bufferForUnPack[resultIndex] = (byte)(inputByte << 2);
                            needBitCountForByte = 2;
                            _previosInputByteRemBits = 0;
                            break;
                        case "2_0":
                            bufferForUnPack[resultIndex] |= (byte)((inputByte >> 4) & 0b_0000_0011);
                            needBitCountForByte = 0;
                            _previosInputByteRemBits = 4;
                            resultIndex++;
                            break;
                        case "0_4":
                            _outerStream.Position -= 2;
                            previosByte = _outerStream.ReadByte();
                            _outerStream.Position++;
                            bufferForUnPack[resultIndex] = (byte)((previosByte & 0b_0000_1111) << 4);
                            bufferForUnPack[resultIndex] |= (byte)((inputByte >> 2) & 0b_0000_1111);
                            needBitCountForByte = 0;
                            _previosInputByteRemBits = 2;
                            resultIndex++;
                            break;
                        case "0_2":
                            _outerStream.Position -= 2;
                            previosByte = _outerStream.ReadByte();
                            _outerStream.Position++;
                            bufferForUnPack[resultIndex] = (byte)((previosByte & 0b_0000_0011) << 6);
                            bufferForUnPack[resultIndex] |= (byte)(inputByte & 0b_0011_1111);
                            needBitCountForByte = 0;
                            _previosInputByteRemBits = 0;
                            resultIndex++;
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                }

                if (needBitCountForByte != 0)
                {
                    throw new InvalidOperationException();
                }

                return resultIndex - offset;
            }

            private void WriteByte(int value)
            {
                _outerStream.WriteByte((byte)value);
            }

            public override void Write(byte[] bufferToPack, int offset, int count)
            {
                if (_mode == Mode.Unpack)
                {
                    throw new NotSupportedException();
                }

                if (count < 0 || offset < 0)
                {
                    throw new ArgumentOutOfRangeException();
                }

                if (offset + count > bufferToPack.Length)
                {
                    throw new ArgumentOutOfRangeException();
                }

                if (!_outerStream.CanWrite)
                {
                    throw new InvalidOperationException();
                }

                if (bufferToPack.Length == 0 || count == 0 || offset == bufferToPack.Length)
                {
                    return;
                }

                for (var i = offset; i < count; i++)
                {
                    var currentByteToPack = bufferToPack[i];
                    int lastByte;

                    switch (_previosOutputByteFreeBits)
                    {
                        case 0:
                            WriteByte(currentByteToPack >> 2);
                            WriteByte((currentByteToPack & 0b_0000_0011) << 4);
                            _previosOutputByteFreeBits = 4;
                            break;
                        case 4:
                            _outerStream.Position -= 1;
                            lastByte = _outerStream.ReadByte();
                            _outerStream.Position -= 1;
                            WriteByte(lastByte | (currentByteToPack >> 4));
                            WriteByte((currentByteToPack & 0b_0000_1111) << 2);
                            _previosOutputByteFreeBits = 2;
                            break;
                        case 2:
                            _outerStream.Position -= 1;
                            lastByte = _outerStream.ReadByte();
                            _outerStream.Position -= 1;
                            WriteByte(lastByte | (currentByteToPack >> 6));
                            WriteByte(currentByteToPack & 0b_0011_1111);
                            _previosOutputByteFreeBits = 0;
                            break;
                        default:
                            throw new InvalidOperationException();
                    }
                }
            }

            public override bool CanRead => _mode == Mode.Unpack;

            public override bool CanSeek => false;

            public override bool CanWrite => _mode == Mode.Pack;

            public override long Length => throw new NotSupportedException();

            public override long Position { get; set; }

            #region Nested types

            public enum Mode
            {
                Pack,
                Unpack
            }

            #endregion
        }
    }
}
