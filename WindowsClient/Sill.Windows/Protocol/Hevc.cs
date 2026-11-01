namespace Sill.Windows.Protocol;

// HEVC as the Mac sends it (hvcC style: each NAL behind a big-endian length, the VPS/SPS/PPS apart
// in kind 0) and as Media Foundation's decoder takes it (Annex B: each NAL behind a start code, the
// parameter sets in band before a keyframe).
public static class Hevc
{
    static readonly byte[] StartCode = { 0, 0, 0, 1 };

    /// <summary>The access unit in Annex B; with <paramref name="sets"/>, those first.</summary>
    public static byte[] ToAnnexB(ReadOnlySpan<byte> frame, int nalLengthSize, ParameterSets? sets)
    {
        int size = frame.Length + 64;
        if (sets != null) foreach (var s in sets.Sets) size += s.Length + 4;
        var output = new byte[size];
        int o = 0;
        if (sets != null)
            foreach (var s in sets.Sets)
            {
                StartCode.CopyTo(output, o); o += 4;
                s.CopyTo(output, o); o += s.Length;
            }
        int i = 0;
        while (i + nalLengthSize <= frame.Length)
        {
            int len = 0;
            for (int k = 0; k < nalLengthSize; k++) len = (len << 8) | frame[i + k];
            i += nalLengthSize;
            if (len <= 0 || i + len > frame.Length) break;
            if (o + 4 + len > output.Length) Array.Resize(ref output, o + 4 + len + 1024);
            StartCode.CopyTo(output, o); o += 4;
            frame.Slice(i, len).CopyTo(output.AsSpan(o)); o += len;
            i += len;
        }
        Array.Resize(ref output, o);
        return output;
    }

    /// <summary>The picture's size from an SPS NAL (its header included): the luma samples less the
    /// conformance window, so what is meant to be seen. Null when it does not parse.</summary>
    public static (int width, int height)? PictureSize(byte[] sps)
    {
        try
        {
            var r = new BitReader(Unescape(sps));
            r.Skip(16);                              // NAL header
            r.Skip(4);                               // sps_video_parameter_set_id
            int maxSubLayersMinus1 = (int)r.Bits(3);
            r.Skip(1);                               // sps_temporal_id_nesting_flag
            // profile_tier_level(1, maxSubLayersMinus1)
            r.Skip(2 + 1 + 5 + 32 + 4 + 43 + 1);     // general profile: space, tier, idc, compat flags, four flags, 43 + 1 bits
            r.Skip(8);                               // general_level_idc
            var profilePresent = new bool[maxSubLayersMinus1];
            var levelPresent = new bool[maxSubLayersMinus1];
            for (int i = 0; i < maxSubLayersMinus1; i++) { profilePresent[i] = r.Bit(); levelPresent[i] = r.Bit(); }
            if (maxSubLayersMinus1 > 0) for (int i = maxSubLayersMinus1; i < 8; i++) r.Skip(2);
            for (int i = 0; i < maxSubLayersMinus1; i++)
            {
                if (profilePresent[i]) r.Skip(88);
                if (levelPresent[i]) r.Skip(8);
            }
            r.Ue();                                  // sps_seq_parameter_set_id
            uint chroma = r.Ue();
            if (chroma == 3) r.Skip(1);
            int width = (int)r.Ue(), height = (int)r.Ue();
            if (r.Bit())
            {
                int subW = chroma is 1 or 2 ? 2 : 1, subH = chroma == 1 ? 2 : 1;
                int left = (int)r.Ue(), right = (int)r.Ue(), top = (int)r.Ue(), bottom = (int)r.Ue();
                width -= subW * (left + right);
                height -= subH * (top + bottom);
            }
            return width > 0 && height > 0 && width < 32768 && height < 32768 ? (width, height) : null;
        }
        catch (IndexOutOfRangeException) { return null; }
    }

    /// <summary>The SPS among the sets (NAL type 33).</summary>
    public static byte[]? Sps(ParameterSets sets) => sets.Sets.FirstOrDefault(s => s.Length > 2 && ((s[0] >> 1) & 0x3F) == 33);

    static byte[] Unescape(byte[] nal)
    {
        var o = new List<byte>(nal.Length);
        int zeros = 0;
        foreach (var b in nal)
        {
            if (zeros >= 2 && b == 3) { zeros = 0; continue; }
            o.Add(b);
            zeros = b == 0 ? zeros + 1 : 0;
        }
        return o.ToArray();
    }

    sealed class BitReader
    {
        readonly byte[] data;
        int pos;
        public BitReader(byte[] d) { data = d; }
        public bool Bit()
        {
            bool b = ((data[pos >> 3] >> (7 - (pos & 7))) & 1) != 0;
            pos++;
            return b;
        }
        public uint Bits(int n)
        {
            uint v = 0;
            for (int i = 0; i < n; i++) v = (v << 1) | (Bit() ? 1u : 0u);
            return v;
        }
        public void Skip(int n)
        {
            pos += n;
            if ((pos - 1) >> 3 >= data.Length) throw new IndexOutOfRangeException();
        }
        public uint Ue()
        {
            int zeros = 0;
            while (!Bit()) { if (++zeros > 31) throw new IndexOutOfRangeException(); }
            return zeros == 0 ? 0 : (1u << zeros) - 1 + Bits(zeros);
        }
    }
}
