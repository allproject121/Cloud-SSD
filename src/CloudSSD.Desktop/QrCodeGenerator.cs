using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;

namespace CloudSSD.Desktop;

/// <summary>
/// Lightweight, zero-dependency QR Code generator (ISO/IEC 18004) in pure C#.
/// Produces crisp WPF BitmapSources for instant pairing over local Wi-Fi.
/// </summary>
public static class QrCodeGenerator
{
    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static QrCodeGenerator()
    {
        int val = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)val;
            Exp[i + 255] = (byte)val;
            Log[val] = (byte)i;
            val <<= 1;
            if ((val & 256) != 0) val ^= 0x11D; // Primitive polynomial x^8 + x^4 + x^3 + x^2 + 1
        }
    }

    private static byte GfMul(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;
        return Exp[Log[a] + Log[b]];
    }

    // Version definitions: (Version, TotalCodewords, DataCodewordsL, EcCodewordsL, AlignPos)
    private record QrVersion(int Version, int TotalBytes, int DataBytes, int EcBytes, int[] AlignPos);

    private static readonly QrVersion[] Versions =
    [
        new(1, 26, 19, 7, []),
        new(2, 44, 34, 10, [6, 18]),
        new(3, 70, 55, 15, [6, 22]),
        new(4, 100, 80, 20, [6, 26]),
        new(5, 134, 108, 26, [6, 30]),
        new(6, 172, 136, 36, [6, 34])
    ];

    public static BitmapSource GenerateQrBitmap(string content, int pixelsPerModule = 8)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(content);

        // Find smallest version that fits data (Mode: 4 bits, Count: 8 bits)
        QrVersion? ver = null;
        foreach (var v in Versions)
        {
            if (utf8.Length <= v.DataBytes - 3) // 3 bytes reserved for mode, count & pad
            {
                ver = v;
                break;
            }
        }
        ver ??= Versions[^1];

        // 1. Data Bitstream Encoding (Byte Mode: 0100)
        var bitList = new List<bool>();
        AddBits(bitList, 0b0100, 4); // Byte mode
        AddBits(bitList, utf8.Length, 8); // Character count indicator
        foreach (byte b in utf8) AddBits(bitList, b, 8);

        // Terminator: up to 4 zeroes
        int padZeros = Math.Min(4, ver.DataBytes * 8 - bitList.Count);
        AddBits(bitList, 0, padZeros);

        // Pad to byte boundary
        while (bitList.Count % 8 != 0) bitList.Add(false);

        // Byte padding (0xEC, 0x11 alternating)
        byte[] padBytes = [0xEC, 0x11];
        int padIdx = 0;
        while (bitList.Count < ver.DataBytes * 8)
        {
            AddBits(bitList, padBytes[padIdx % 2], 8);
            padIdx++;
        }

        // Convert bits to data codewords
        byte[] dataCodewords = new byte[ver.DataBytes];
        for (int i = 0; i < ver.DataBytes; i++)
        {
            byte b = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                if (bitList[i * 8 + bit]) b |= (byte)(1 << (7 - bit));
            }
            dataCodewords[i] = b;
        }

        // 2. Reed-Solomon Error Correction Codewords
        byte[] ecCodewords = CalculateEc(dataCodewords, ver.EcBytes);

        // Combined codewords
        byte[] allCodewords = new byte[ver.TotalBytes];
        Array.Copy(dataCodewords, 0, allCodewords, 0, dataCodewords.Length);
        Array.Copy(ecCodewords, 0, allCodewords, dataCodewords.Length, ecCodewords.Length);

        // 3. Matrix Placement
        int size = 17 + 4 * ver.Version;
        bool?[,] matrix = new bool?[size, size];

        // Position Detection Patterns
        PlaceFinderPattern(matrix, 0, 0);
        PlaceFinderPattern(matrix, size - 7, 0);
        PlaceFinderPattern(matrix, 0, size - 7);

        // Separators around finders
        PlaceSeparators(matrix, size);

        // Alignment Patterns
        if (ver.AlignPos.Length > 0)
        {
            foreach (int r in ver.AlignPos)
            {
                foreach (int c in ver.AlignPos)
                {
                    // Skip finders
                    if ((r == 6 && c == 6) || (r == 6 && c == size - 7) || (r == size - 7 && c == 6)) continue;
                    PlaceAlignmentPattern(matrix, r, c);
                }
            }
        }

        // Timing Patterns
        for (int i = 8; i < size - 8; i++)
        {
            matrix[6, i] ??= (i % 2 == 0);
            matrix[i, 6] ??= (i % 2 == 0);
        }

        // Dark Module
        matrix[4 * ver.Version + 9, 8] = true;

        // Reserve format info area
        for (int i = 0; i < 9; i++)
        {
            matrix[8, i] ??= false;
            matrix[i, 8] ??= false;
        }
        for (int i = 0; i < 8; i++)
        {
            matrix[8, size - 1 - i] ??= false;
            matrix[size - 1 - i, 8] ??= false;
        }

        // Place Data Bits in zig-zag order
        var allBits = new List<bool>();
        foreach (byte b in allCodewords)
        {
            for (int bit = 7; bit >= 0; bit--) allBits.Add(((b >> bit) & 1) != 0);
        }

        int bitIndex = 0;
        int col = size - 1;
        bool upward = true;

        while (col > 0)
        {
            if (col == 6) col--; // Skip vertical timing line

            for (int r = 0; r < size; r++)
            {
                int row = upward ? (size - 1 - r) : r;
                for (int c = 0; c < 2; c++)
                {
                    int currentC = col - c;
                    if (matrix[row, currentC] == null)
                    {
                        bool bit = bitIndex < allBits.Count && allBits[bitIndex++];
                        // Apply Mask Pattern 0: (row + col) % 2 == 0
                        bool mask = ((row + currentC) % 2 == 0);
                        matrix[row, currentC] = bit ^ mask;
                    }
                }
            }
            col -= 2;
            upward = !upward;
        }

        // Format Information: EC Level L (01) + Mask 0 (000) = 01000
        // BCH 15,5 error correction gives 0x77C4 (unmasked), masked with 0x5412 = 0x23D6
        int formatBits = 0x23D6;
        PlaceFormatInfo(matrix, formatBits, size);

        // 4. Render to WPF BitmapSource with 4-module quiet zone
        int quietZone = 4;
        int fullSize = size + quietZone * 2;
        int imgWidth = fullSize * pixelsPerModule;
        int imgHeight = fullSize * pixelsPerModule;
        int stride = (imgWidth * 32 + 31) / 32 * 4;
        byte[] pixels = new byte[stride * imgHeight];

        // Background white
        Array.Fill(pixels, (byte)255);

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (matrix[r, c] == true)
                {
                    int startX = (c + quietZone) * pixelsPerModule;
                    int startY = (r + quietZone) * pixelsPerModule;

                    for (int y = 0; y < pixelsPerModule; y++)
                    {
                        for (int x = 0; x < pixelsPerModule; x++)
                        {
                            int offset = (startY + y) * stride + (startX + x) * 4;
                            // Black module (B=0, G=0, R=0, A=255)
                            pixels[offset] = 0;
                            pixels[offset + 1] = 0;
                            pixels[offset + 2] = 0;
                            pixels[offset + 3] = 255;
                        }
                    }
                }
            }
        }

        var bmp = BitmapSource.Create(imgWidth, imgHeight, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    private static void AddBits(List<bool> list, int value, int count)
    {
        for (int i = count - 1; i >= 0; i--) list.Add(((value >> i) & 1) != 0);
    }

    private static byte[] CalculateEc(byte[] data, int ecCount)
    {
        byte[] gen = new byte[ecCount + 1];
        gen[0] = 1;
        for (int i = 0; i < ecCount; i++)
        {
            byte root = Exp[i];
            for (int j = i + 1; j > 0; j--) gen[j] = (byte)(gen[j] ^ GfMul(gen[j - 1], root));
        }

        byte[] remainder = new byte[ecCount];
        for (int i = 0; i < data.Length; i++)
        {
            byte lead = (byte)(data[i] ^ remainder[0]);
            for (int j = 0; j < ecCount - 1; j++)
            {
                remainder[j] = (byte)(remainder[j + 1] ^ GfMul(gen[j + 1], lead));
            }
            remainder[ecCount - 1] = GfMul(gen[ecCount], lead);
        }
        return remainder;
    }

    private static void PlaceFinderPattern(bool?[,] m, int startR, int startC)
    {
        for (int r = 0; r < 7; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                bool isDark = (r == 0 || r == 6 || c == 0 || c == 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
                m[startR + r, startC + c] = isDark;
            }
        }
    }

    private static void PlaceSeparators(bool?[,] m, int size)
    {
        // Top-left
        for (int i = 0; i < 8; i++)
        {
            m[7, i] = false;
            m[i, 7] = false;
            // Top-right
            m[7, size - 1 - i] = false;
            m[i, size - 8] = false;
            // Bottom-left
            m[size - 8, i] = false;
            m[size - 1 - i, 7] = false;
        }
    }

    private static void PlaceAlignmentPattern(bool?[,] m, int centerR, int centerC)
    {
        for (int r = -2; r <= 2; r++)
        {
            for (int c = -2; c <= 2; c++)
            {
                bool dark = (Math.Abs(r) == 2 || Math.Abs(c) == 2 || (r == 0 && c == 0));
                m[centerR + r, centerC + c] = dark;
            }
        }
    }

    private static void PlaceFormatInfo(bool?[,] m, int bits, int size)
    {
        for (int i = 0; i < 15; i++)
        {
            bool bit = ((bits >> i) & 1) != 0;

            // Top-left
            if (i <= 5) m[8, i] = bit;
            else if (i == 6) m[8, 7] = bit;
            else if (i <= 8) m[8, 8] = bit;
            else if (i == 9) m[7, 8] = bit;
            else m[14 - i, 8] = bit;

            // Right & Bottom
            if (i < 8) m[size - 1 - i, 8] = bit;
            else m[8, size - 15 + i] = bit;
        }
    }
}
