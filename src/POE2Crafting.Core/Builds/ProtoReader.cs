using System.Text;

namespace POE2Crafting.Core.Builds;

/// <summary>One field of a protobuf message: number, varint value (wire type 0) or bytes (wire type 2).</summary>
public readonly record struct ProtoField(int Number, int WireType, ulong Value, ReadOnlyMemory<byte> Bytes)
{
    public string Text => Encoding.UTF8.GetString(Bytes.Span);
    public int Int => (int)Value;
    public IReadOnlyList<ProtoField> Message => ProtoReader.Read(Bytes);
}

/// <summary>
/// Minimal protobuf wire format reader for poe.ninja's build search (no schema library needed: the few messages used are read by field number,
/// see <see cref="BuildSearchResult"/>). Fixed 32/64-bit fields are skipped (their bytes are kept).
/// </summary>
public static class ProtoReader
{
    public static IReadOnlyList<ProtoField> Read(ReadOnlyMemory<byte> data)
    {
        var fields = new List<ProtoField>();
        var span = data.Span;
        int i = 0;
        while (i < span.Length)
        {
            ulong key = Varint(span, ref i);
            int number = (int)(key >> 3), wire = (int)(key & 7);
            switch (wire)
            {
                case 0:
                    fields.Add(new ProtoField(number, wire, Varint(span, ref i), ReadOnlyMemory<byte>.Empty));
                    break;
                case 2:
                    int length = (int)Varint(span, ref i);
                    fields.Add(new ProtoField(number, wire, 0, data.Slice(i, length)));
                    i += length;
                    break;
                case 1:
                    fields.Add(new ProtoField(number, wire, 0, data.Slice(i, 8)));
                    i += 8;
                    break;
                case 5:
                    fields.Add(new ProtoField(number, wire, 0, data.Slice(i, 4)));
                    i += 4;
                    break;
                default:
                    throw new FormatException($"Unsupported protobuf wire type {wire} at byte {i}.");
            }
        }
        return fields;
    }

    /// <summary>A packed repeated varint field (int32/bool lists).</summary>
    public static List<int> PackedVarints(ReadOnlyMemory<byte> data)
    {
        var values = new List<int>();
        var span = data.Span;
        for (int i = 0; i < span.Length;) values.Add((int)Varint(span, ref i));
        return values;
    }

    public static ulong Varint(ReadOnlySpan<byte> span, ref int i)
    {
        ulong result = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte b = span[i++];
            result |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80) return result;
        }
    }

    /// <summary>The first string/bytes field with this number as text, or null.</summary>
    public static string? FirstText(this IReadOnlyList<ProtoField> fields, int number)
    {
        foreach (var f in fields)
            if (f.Number == number && f.WireType == 2) return f.Text;
        return null;
    }

    public static IEnumerable<ProtoField> All(this IReadOnlyList<ProtoField> fields, int number) => fields.Where(f => f.Number == number);
}
