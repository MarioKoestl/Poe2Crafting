using System.Text;

namespace POE2Crafting.Core.Builds;

/// <summary>
/// poe.ninja's binary dictionaries (GET /poe2/api/builds/dictionary/&lt;hash&gt;), format reverse-engineered from their site script:
/// "NDIC" = the values (names) a dimension key indexes; "NOVL" = per-value properties (e.g. the item type "Gloves" of "Rare Gloves").
/// The checksums in the headers are not verified.
/// </summary>
public static class NinjaDictionary
{
    private static readonly byte[] ValuesMagic = "NDIC"u8.ToArray(), PropertiesMagic = "NOVL"u8.ToArray();

    /// <summary>NDIC: header (36 bytes: magic, version 2, ..., count @12, sync entries @28, lengths size @32), sync table (8 bytes each), varint lengths, UTF-8 strings.</summary>
    public static IReadOnlyList<string> Values(byte[] blob)
    {
        if (!blob.AsSpan().StartsWith(ValuesMagic)) throw new FormatException("Not a poe.ninja NDIC dictionary.");
        int count = BitConverter.ToInt32(blob, 12), sync = BitConverter.ToInt32(blob, 28), lengths = BitConverter.ToInt32(blob, 32);
        int lengthPos = 36 + sync * 8, textPos = lengthPos + lengths;
        var values = new string[count];
        for (int n = 0; n < count; n++)
        {
            int length = (int)ProtoReader.Varint(blob, ref lengthPos);
            values[n] = Encoding.UTF8.GetString(blob, textPos, length);
            textPos += length;
        }
        return values;
    }

    /// <summary>NOVL: property name → one value per dictionary entry (header 28 bytes: property count @12, value count @16).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Properties(byte[] blob)
    {
        if (!blob.AsSpan().StartsWith(PropertiesMagic)) throw new FormatException("Not a poe.ninja NOVL dictionary overlay.");
        int propertyCount = BitConverter.ToInt32(blob, 12), valueCount = BitConverter.ToInt32(blob, 16);
        int pos = 28;
        string Text()
        {
            int length = BitConverter.ToInt32(blob, pos);
            pos += 4;
            var text = Encoding.UTF8.GetString(blob, pos, length);
            pos += length;
            return text;
        }
        Text(); // overlay id
        Text(); // hash of the values it belongs to
        var properties = new Dictionary<string, IReadOnlyList<string>>();
        for (int p = 0; p < propertyCount; p++)
        {
            var name = Text();
            int offsets = pos, data = offsets + (valueCount + 1) * 4;
            var values = new string[valueCount];
            for (int v = 0; v < valueCount; v++)
            {
                int start = BitConverter.ToInt32(blob, offsets + v * 4), end = BitConverter.ToInt32(blob, offsets + (v + 1) * 4);
                values[v] = Encoding.UTF8.GetString(blob, data + start, end - start);
            }
            pos = data + BitConverter.ToInt32(blob, offsets + valueCount * 4);
            properties[name] = values;
        }
        return properties;
    }
}
