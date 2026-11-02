using System.Text;

namespace Gather.Signal;

// QR Model 2, version 10, level L, byte mode, mask 0. A deliberately bounded
// encoder for provisioning URIs; matrices are checked against ReportLab in QA.
// No remote QR service ever receives the device-link secret.
public static class LinkQrCode
{
    public const int Size = 57;
    public static bool[,] Encode(string text)
    {
        byte[] value = Encoding.UTF8.GetBytes(text);
        if (value.Length > 271) throw new ArgumentException("Signal link is too long for this QR encoder.");
        var bits = new List<bool>();
        void Append(int number, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((number >> i) & 1) != 0); }
        Append(4, 4); Append(value.Length, 16);
        foreach (byte b in value) Append(b, 8);
        Append(0, Math.Min(4, 274 * 8 - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        for (int pad = 0; bits.Count < 274 * 8; pad++) Append(pad % 2 == 0 ? 0xEC : 0x11, 8);
        var data = new byte[274];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) data[i / 8] |= (byte)(1 << (7 - i % 8));
        var blocks = new List<byte[]>(); var checks = new List<byte[]>();
        int offset = 0;
        for (int i = 0; i < 4; i++)
        {
            int length = i < 2 ? 68 : 69;
            var block = data.AsSpan(offset, length).ToArray(); offset += length;
            blocks.Add(block); checks.Add(Remainder(block, 18));
        }
        var codewords = new List<byte>();
        for (int i = 0; i < 69; i++) foreach (var block in blocks) if (i < block.Length) codewords.Add(block[i]);
        for (int i = 0; i < 18; i++) foreach (var check in checks) codewords.Add(check[i]);
        var matrix = new bool[Size, Size]; var function = new bool[Size, Size];
        void Set(int x, int y, bool dark) { if (x < 0 || y < 0 || x >= Size || y >= Size) return; matrix[x, y] = dark; function[x, y] = true; }
        for (int i = 0; i < Size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
        foreach (var (cx, cy) in new[] { (3, 3), (Size - 4, 3), (3, Size - 4) })
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
            { int d = Math.Max(Math.Abs(dx), Math.Abs(dy)); Set(cx + dx, cy + dy, d != 2 && d != 4); }
        int[] align = [6, 28, 50];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
        {
            if ((i == 0 && j == 0) || (i == 0 && j == 2) || (i == 2 && j == 0)) continue;
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) Set(align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }
        const int format = 0x77C4; // L (01), mask 000, BCH, XOR 0x5412.
        bool FormatBit(int i) => ((format >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) Set(8, i, FormatBit(i));
        Set(8, 7, FormatBit(6)); Set(8, 8, FormatBit(7)); Set(7, 8, FormatBit(8));
        for (int i = 9; i < 15; i++) Set(14 - i, 8, FormatBit(i));
        for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, FormatBit(i));
        for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, FormatBit(i));
        Set(8, Size - 8, true);
        int versionRemainder = 10;
        for (int i = 0; i < 12; i++) versionRemainder = (versionRemainder << 1) ^ ((versionRemainder >> 11) * 0x1F25);
        int version = (10 << 12) | versionRemainder;
        for (int i = 0; i < 18; i++) { int a = Size - 11 + i % 3, b = i / 3; bool dark = ((version >> i) & 1) != 0; Set(a, b, dark); Set(b, a, dark); }
        int bit = 0;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vertical = 0; vertical < Size; vertical++)
            {
                int y = ((right + 1) & 2) == 0 ? Size - 1 - vertical : vertical;
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    if (function[x, y]) continue;
                    bool dark = bit < codewords.Count * 8 && ((codewords[bit / 8] >> (7 - bit % 8)) & 1) != 0;
                    matrix[x, y] = dark ^ ((x + y) % 2 == 0); bit++;
                }
            }
        }
        if (bit != codewords.Count * 8) throw new InvalidOperationException("Invalid QR layout.");
        return matrix;
    }
    private static byte Multiply(int x, int y)
    {
        int product = 0;
        for (int i = 7; i >= 0; i--) { product = (product << 1) ^ ((product >> 7) * 0x11D); product ^= ((y >> i) & 1) * x; }
        return (byte)product;
    }
    private static byte[] Remainder(byte[] data, int degree)
    {
        var divisor = new byte[degree]; divisor[^1] = 1;
        byte root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < divisor.Length; j++)
            { divisor[j] = Multiply(divisor[j], root); if (j + 1 < divisor.Length) divisor[j] ^= divisor[j + 1]; }
            root = Multiply(root, 2);
        }
        var result = new byte[degree];
        foreach (byte value in data)
        {
            byte factor = (byte)(value ^ result[0]);
            Array.Copy(result, 1, result, 0, degree - 1); result[^1] = 0;
            for (int i = 0; i < degree; i++) result[i] ^= Multiply(divisor[i], factor);
        }
        return result;
    }
}
