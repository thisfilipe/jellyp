using System.Buffers.Binary;
using System.Numerics;

namespace Jellyfin.Plugin.JellyP.Conversion;

// Skia/libjpeg can report Success after an early EOI and fill missing scanlines.
// Validate baseline and progressive Huffman entropy independently before trusting it.
// Progressive state retains only coefficient occupancy, never full coefficient values.
internal static class JpegCompletenessValidator
{
    internal static bool Validate(ReadOnlySpan<byte> source)
    {
        var tables = new HuffmanTable?[8];
        var components = new Dictionary<byte, Component>();
        var seen = new HashSet<byte>();
        var offset = 2;
        int width = 0, height = 0, maxH = 0, maxV = 0, restartInterval = 0, scans = 0;
        var progressive = false;
        long work = 0;
        while (offset < source.Length - 1)
        {
            if (source[offset++] != 255) return false;
            while (offset < source.Length && source[offset] == 255) offset++;
            if (offset >= source.Length) return false;
            var marker = source[offset++];
            if (marker == 0xd9) return offset == source.Length && components.Count > 0
                && (progressive ? components.Values.All(static component => component.Bits[0] >= 0) : seen.Count == components.Count);
            if (offset > source.Length - 2) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, 2));
            if (length < 2 || length > source.Length - offset) return false;
            var payload = source.Slice(offset + 2, length - 2);
            offset += length;
            if (marker is 0xc0 or 0xc2)
            {
                progressive = marker == 0xc2;
                if (components.Count != 0 || payload.Length < 6 || payload[0] != 8) return false;
                height = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(1, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(3, 2));
                var count = payload[5];
                if (width == 0 || height == 0 || width > WebpImageConverter.MaximumDimension || height > WebpImageConverter.MaximumDimension
                    || (long)width * height > WebpImageConverter.MaximumPixelCount || count is not (1 or 3) || payload.Length != 6 + (count * 3)) return false;
                for (var i = 0; i < count; i++)
                {
                    var id = payload[6 + (i * 3)];
                    var h = payload[7 + (i * 3)] >> 4;
                    var v = payload[7 + (i * 3)] & 15;
                    if (h is < 1 or > 4 || v is < 1 or > 4 || !components.TryAdd(id, new Component(h, v))) return false;
                    maxH = Math.Max(maxH, h); maxV = Math.Max(maxV, v);
                }
                if (components.Values.Sum(static component => component.H * component.V) > 10) return false;
                long stateBlocks = 0;
                foreach (var component in components.Values)
                {
                    component.Stride = DivideRoundUp(width, maxH * 8) * component.H;
                    component.VisibleWidth = DivideRoundUp(width * component.H, maxH * 8);
                    component.VisibleHeight = DivideRoundUp(height * component.V, maxV * 8);
                    var blocks = component.Stride * DivideRoundUp(height, maxV * 8) * component.V;
                    stateBlocks += blocks;
                    // At most 16.8 MB of progressive occupancy state, checked before allocation.
                    if (stateBlocks > 2_100_000) return false;
                }
                if (progressive)
                    foreach (var component in components.Values)
                        component.Nonzero = new ulong[component.Stride * DivideRoundUp(height, maxV * 8) * component.V];
            }
            else if (marker == 0xc4)
            {
                var position = 0;
                while (position < payload.Length)
                {
                    var selector = payload[position++];
                    var kind = selector >> 4;
                    var index = selector & 15;
                    if (kind > 1 || index > 3 || payload.Length - position < 16) return false;
                    var counts = payload.Slice(position, 16);
                    position += 16;
                    var count = 0;
                    foreach (var value in counts) count += value;
                    if (count == 0 || count > 256 || count > payload.Length - position) return false;
                    var table = HuffmanTable.Create(counts, payload.Slice(position, count));
                    if (table is null) return false;
                    tables[(kind * 4) + index] = table;
                    position += count;
                }
            }
            else if (marker == 0xdd)
            {
                if (payload.Length != 2) return false;
                restartInterval = BinaryPrimitives.ReadUInt16BigEndian(payload);
            }
            else if (marker == 0xda)
            {
                if (payload.Length < 6 || components.Count == 0 || ++scans > 512) return false;
                var count = payload[0];
                var ss = payload[^3];
                var se = payload[^2];
                var ah = payload[^1] >> 4;
                var al = payload[^1] & 15;
                if (count < 1 || count > components.Count || payload.Length != 4 + (2 * count)) return false;
                if (progressive)
                {
                    if (ss > se || se > 63 || (ss == 0 ? se != 0 : count != 1)
                        || ah > 13 || al > 13 || (ah != 0 && al != ah - 1)) return false;
                }
                else if (ss != 0 || se != 63 || ah != 0 || al != 0) return false;
                var scan = new (Component Component, HuffmanTable? Dc, HuffmanTable? Ac)[count];
                var scanIds = new HashSet<byte>();
                var blocksPerMcu = 0;
                for (var i = 0; i < count; i++)
                {
                    var id = payload[1 + (i * 2)];
                    var selector = payload[2 + (i * 2)];
                    var dc = selector >> 4;
                    var ac = selector & 15;
                    if (dc > 3 || ac > 3 || !components.TryGetValue(id, out var component) || !scanIds.Add(id)) return false;
                    if (progressive)
                    {
                        if (ss > 0 && component.Bits[0] < 0) return false;
                        for (var coefficient = ss; coefficient <= se; coefficient++)
                        {
                            if (ah == 0 ? component.Bits[coefficient] != -1 : component.Bits[coefficient] != ah) return false;
                            component.Bits[coefficient] = al;
                        }
                    }
                    else if (!seen.Add(id)) return false;
                    var dcTable = tables[dc];
                    var acTable = tables[4 + ac];
                    if ((!progressive || (ss == 0 && ah == 0)) && dcTable is null) return false;
                    if ((!progressive || ss > 0) && acTable is null) return false;
                    scan[i] = (component, dcTable, acTable);
                    blocksPerMcu += component.H * component.V;
                }
                if (count > 1 && blocksPerMcu > 10) return false;
                var mcuColumns = count == 1 ? scan[0].Component.VisibleWidth : DivideRoundUp(width, maxH * 8);
                var mcuRows = count == 1 ? scan[0].Component.VisibleHeight : DivideRoundUp(height, maxV * 8);
                var mcuCount = mcuColumns * mcuRows;
                var reader = new EntropyReader(source, offset);
                var restart = 0;
                var eobRun = 0;
                for (var mcu = 0; mcu < mcuCount; mcu++)
                {
                    if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
                    {
                        if (eobRun != 0 || !reader.ConsumeRestart(restart)) return false;
                        restart = (restart + 1) & 7;
                    }
                    foreach (var entry in scan)
                    {
                        var blocks = count == 1 ? 1 : entry.Component.H * entry.Component.V;
                        for (var block = 0; block < blocks; block++)
                        {
                            // Bound aggregate entropy-validation effort independently of encoded size.
                            if (++work > 128_000_000) return false;
                            if (progressive)
                            {
                                if (ss == 0)
                                {
                                    if (ah == 0)
                                    {
                                        var bits = entry.Dc!.Decode(ref reader);
                                        if (bits is < 0 or > 11 || !reader.DiscardBits(bits)) return false;
                                    }
                                    else if (!reader.DiscardBits(1)) return false;
                                }
                                else
                                {
                                    // AC scans are noninterleaved: each MCU is one visible block.
                                    var index = ((mcu / mcuColumns) * entry.Component.Stride) + (mcu % mcuColumns);
                                    ref var mask = ref entry.Component.Nonzero[index];
                                    if (!DecodeProgressiveAc(ref reader, entry.Ac!, ss, se, ah != 0, ref mask, ref eobRun, ref work)) return false;
                                }
                            }
                            else if (!DecodeBaselineBlock(ref reader, entry.Dc!, entry.Ac!, ref work)) return false;
                        }
                    }
                }
                if (eobRun != 0 || !reader.Finish(out offset)) return false;
            }
            // Metadata arriving after the first scan was not checked by the header gate.
            else if (scans > 0 && marker is 0xe1 or 0xe2 or 0xee) return false;
            else if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc)) return false;
            else if (marker is 0x00 or 0xd8 || marker is >= 0xd0 and <= 0xd7) return false;
        }
        return false;
    }

    private static int DivideRoundUp(int value, int divisor) => (value + divisor - 1) / divisor;
    private sealed class Component(int h, int v)
    {
        internal int H { get; } = h;
        internal int V { get; } = v;
        internal int Stride { get; set; }
        internal int VisibleWidth { get; set; }
        internal int VisibleHeight { get; set; }
        internal int[] Bits { get; } = Enumerable.Repeat(-1, 64).ToArray();
        internal ulong[] Nonzero { get; set; } = [];
    }

    private static bool DecodeBaselineBlock(ref EntropyReader reader, HuffmanTable dc, HuffmanTable ac, ref long work)
    {
        var bits = dc.Decode(ref reader);
        if (bits is < 0 or > 11 || !reader.DiscardBits(bits)) return false;
        var coefficient = 1;
        while (coefficient < 64)
        {
            if (++work > 128_000_000) return false;
            var symbol = ac.Decode(ref reader);
            if (symbol < 0) return false;
            if (symbol == 0) return true;
            if (symbol == 0xf0) { coefficient += 16; if (coefficient > 64) return false; continue; }
            var size = symbol & 15;
            coefficient += symbol >> 4;
            if (size is < 1 or > 10 || coefficient >= 64 || !reader.DiscardBits(size)) return false;
            coefficient++;
        }
        return true;
    }

    private static bool DecodeProgressiveAc(ref EntropyReader reader, HuffmanTable table, int start, int end,
        bool refinement, ref ulong mask, ref int eobRun, ref long work)
    {
        var coefficient = start;
        if (eobRun == 0)
        {
            while (coefficient <= end)
            {
                if (++work > 128_000_000) return false;
                var symbol = table.Decode(ref reader);
                if (symbol < 0) return false;
                var run = symbol >> 4;
                var size = symbol & 15;
                if (size == 0 && run < 15)
                {
                    var extra = reader.ReadBits(run);
                    if (extra < 0) return false;
                    eobRun = (1 << run) + extra;
                    break;
                }
                if (refinement)
                {
                    if (size is not (0 or 1)) return false;
                    if (size == 1 && !reader.DiscardBits(1)) return false; // New coefficient sign.
                    // A ZRL skips 16 still-zero positions; existing coefficients consume correction bits.
                    var zeros = size == 0 ? 16 : run;
                    while (coefficient <= end)
                    {
                        if (++work > 128_000_000) return false;
                        if ((mask & (1UL << coefficient)) != 0)
                        {
                            if (!reader.DiscardBits(1)) return false;
                        }
                        else
                        {
                            if (zeros == 0) break;
                            zeros--;
                            if (size == 0 && zeros == 0) { coefficient++; break; }
                        }
                        coefficient++;
                    }
                    if (zeros != 0) return false;
                    if (size == 1)
                    {
                        if (coefficient > end) return false;
                        mask |= 1UL << coefficient++;
                    }
                }
                else
                {
                    if (size == 0) coefficient += 16;
                    else
                    {
                        coefficient += run;
                        if (size > 10 || coefficient > end || !reader.DiscardBits(size)) return false;
                        mask |= 1UL << coefficient++;
                    }
                    if (coefficient > end + 1) return false;
                }
            }
        }
        if (eobRun > 0)
        {
            if (refinement)
            {
                var band = (ulong.MaxValue << coefficient) & (ulong.MaxValue >> (63 - end));
                if (!reader.DiscardBits(BitOperations.PopCount(mask & band))) return false;
            }
            eobRun--;
        }
        return true;
    }

    private sealed class HuffmanTable
    {
        private readonly int[] _minimum = new int[17];
        private readonly int[] _maximum = new int[17];
        private readonly int[] _valueOffsets = new int[17];
        private readonly byte[] _values;
        private HuffmanTable(byte[] values) => _values = values;

        internal static HuffmanTable? Create(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            var table = new HuffmanTable(values.ToArray());
            var code = 0;
            var index = 0;
            for (var length = 1; length <= 16; length++)
            {
                var count = counts[length - 1];
                if (count > 0 && code + count >= (1 << length)) return null; // JPEG reserves the all-ones code for padding.
                table._minimum[length] = code;
                table._maximum[length] = count == 0 ? -1 : code + count - 1;
                table._valueOffsets[length] = index - code;
                index += count;
                code = (code + count) << 1;
            }
            return table;
        }

        internal int Decode(ref EntropyReader reader)
        {
            var code = 0;
            for (var length = 1; length <= 16; length++)
            {
                var bit = reader.ReadBit();
                if (bit < 0) return -1;
                code = (code << 1) | bit;
                if (code >= _minimum[length] && code <= _maximum[length]) return _values[_valueOffsets[length] + code];
            }
            return -1;
        }
    }

    private ref struct EntropyReader(ReadOnlySpan<byte> source, int position)
    {
        private readonly ReadOnlySpan<byte> _source = source;
        private int _position = position;
        private int _remaining;
        private int _value;

        internal int ReadBit()
        {
            if (_remaining == 0)
            {
                if (_position >= _source.Length) return -1;
                _value = _source[_position];
                if (_value == 255)
                {
                    if (_position + 1 >= _source.Length || _source[_position + 1] != 0) return -1;
                    _position++;
                }
                _position++;
                _remaining = 8;
            }
            return (_value >> --_remaining) & 1;
        }

        internal int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                var bit = ReadBit();
                if (bit < 0) return -1;
                value = (value << 1) | bit;
            }
            return value;
        }

        internal bool DiscardBits(int count)
        {
            for (var i = 0; i < count; i++) if (ReadBit() < 0) return false;
            return true;
        }

        private bool Align()
        {
            var mask = (1 << _remaining) - 1;
            if ((_value & mask) != mask) return false;
            _remaining = 0;
            return true;
        }

        internal bool ConsumeRestart(int index)
        {
            if (!Align() || _position + 1 >= _source.Length || _source[_position] != 255) return false;
            while (_position < _source.Length && _source[_position] == 255) _position++;
            return _position < _source.Length && _source[_position++] == 0xd0 + index;
        }

        internal bool Finish(out int nextPosition)
        {
            nextPosition = _position;
            return Align() && _position + 1 < _source.Length && _source[_position] == 255 && _source[_position + 1] != 0;
        }
    }
}
