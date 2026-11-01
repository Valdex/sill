using Sill.Windows.Interop;

namespace Sill.TestHost;

/// <summary>TEST ONLY: Media Foundation's synchronous HEVC encoder (the HEVC Video Extensions' on
/// Windows 11), NV12 in, Annex B out, every access unit as soon as it is done.</summary>
public sealed unsafe class HevcEncoder : IDisposable
{
    IntPtr transform;
    readonly int width, height;
    bool providesSamples;
    uint outputSize;
    long time;
    public string Name { get; }

    public HevcEncoder(int width, int height, int fps, int bitrate)
    {
        this.width = width; this.height = height;
        MF.Startup();
        var found = MF.Enumerate(MF.MFT_CATEGORY_VIDEO_ENCODER, MF.MFT_ENUM_FLAG_SYNCMFT | MF.MFT_ENUM_FLAG_LOCALMFT | MF.MFT_ENUM_FLAG_SORTANDFILTER,
                                 MF.MFVideoFormat_NV12, MF.MFVideoFormat_HEVC);
        string? name = null;
        foreach (var (a, n, _, async) in found)
        {
            if (transform == IntPtr.Zero && !async)
            {
                try { transform = MF.Activate(a); name = n; } catch (ComException) { }
            }
            Com.Release(a);
        }
        if (transform == IntPtr.Zero) throw new InvalidOperationException("No synchronous HEVC encoder on this PC.");
        Name = name!;
        var attrs = MF.GetAttributes(transform);
        if (attrs != IntPtr.Zero) { try { MF.SetUInt32(attrs, MF.MF_LOW_LATENCY, 1); } catch (ComException) { } Com.Release(attrs); }

        var output = MF.CreateMediaType();
        MF.SetGuid(output, MF.MF_MT_MAJOR_TYPE, MF.MFMediaType_Video);
        MF.SetGuid(output, MF.MF_MT_SUBTYPE, MF.MFVideoFormat_HEVC);
        MF.SetUInt32(output, MF.MF_MT_AVG_BITRATE, (uint)bitrate);
        MF.SetUInt64(output, MF.MF_MT_FRAME_SIZE, MF.Pack((uint)width, (uint)height));
        MF.SetUInt64(output, MF.MF_MT_FRAME_RATE, MF.Pack((uint)fps, 1));
        MF.SetUInt64(output, MF.MF_MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
        MF.SetUInt32(output, MF.MF_MT_INTERLACE_MODE, MF.MFVideoInterlace_Progressive);
        Com.Check(MF.SetOutputType(transform, output), "encoder SetOutputType");
        Com.Release(output);

        // NV12 as the encoder offers it, with this picture's size and rate.
        int hr = -1;
        for (uint i = 0; MF.GetInputAvailableType(transform, i, out var input) >= 0; i++)
        {
            bool nv12 = MF.TryGetGuid(input, MF.MF_MT_SUBTYPE, out var sub) && sub == MF.MFVideoFormat_NV12;
            if (nv12)
            {
                MF.SetUInt64(input, MF.MF_MT_FRAME_SIZE, MF.Pack((uint)width, (uint)height));
                MF.SetUInt64(input, MF.MF_MT_FRAME_RATE, MF.Pack((uint)fps, 1));
                MF.SetUInt64(input, MF.MF_MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
                MF.SetUInt32(input, MF.MF_MT_INTERLACE_MODE, MF.MFVideoInterlace_Progressive);
                hr = MF.SetInputType(transform, input);
            }
            Com.Release(input);
            if (hr >= 0) break;
        }
        Com.Check(hr, "encoder SetInputType");

        var info = MF.GetOutputStreamInfo(transform);
        providesSamples = (info.dwFlags & MF.MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) != 0;
        outputSize = Math.Max(info.cbSize, (uint)(width * height * 2));
        MF.ProcessMessage(transform, MF.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero);
        MF.ProcessMessage(transform, MF.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero);
    }

    /// <summary>One NV12 picture in; the Annex B access units that came out.</summary>
    public List<byte[]> Encode(ReadOnlySpan<byte> nv12, int fps)
    {
        var outputs = new List<byte[]>();
        var buffer = MF.CreateMemoryBuffer((uint)nv12.Length);
        var sample = MF.CreateSample();
        try
        {
            var p = MF.Lock(buffer, out _, out _);
            nv12.CopyTo(new Span<byte>((void*)p, nv12.Length));
            MF.Unlock(buffer);
            MF.SetCurrentLength(buffer, (uint)nv12.Length);
            MF.AddBuffer(sample, buffer);
            long duration = 10_000_000L / fps;
            MF.SetSampleTime(sample, time);
            MF.SetSampleDuration(sample, duration);
            time += duration;
            int hr = MF.ProcessInput(transform, sample);
            if (hr == MF.MF_E_NOTACCEPTING) { Drain(outputs); hr = MF.ProcessInput(transform, sample); }
            Com.Check(hr, "encoder ProcessInput");
            Drain(outputs);
        }
        finally { Com.Release(sample); Com.Release(buffer); }
        return outputs;
    }

    void Drain(List<byte[]> outputs)
    {
        while (true)
        {
            IntPtr own = IntPtr.Zero, ownBuffer = IntPtr.Zero;
            if (!providesSamples)
            {
                own = MF.CreateSample();
                ownBuffer = MF.CreateMemoryBuffer(outputSize);
                MF.AddBuffer(own, ownBuffer);
            }
            var o = new MFT_OUTPUT_DATA_BUFFER { pSample = own };
            int hr = MF.ProcessOutput(transform, ref o, out _);
            Com.Release(o.pEvents);
            try
            {
                if (hr == MF.MF_E_TRANSFORM_NEED_MORE_INPUT) return;
                if (hr == MF.MF_E_TRANSFORM_STREAM_CHANGE)
                {
                    // Take the output type again as offered.
                    if (MF.GetOutputAvailableType(transform, 0, out var t) >= 0) { MF.SetOutputType(transform, t); Com.Release(t); }
                    continue;
                }
                Com.Check(hr, "encoder ProcessOutput");
                var b = MF.ConvertToContiguousBuffer(o.pSample);
                var q = MF.Lock(b, out _, out uint len);
                var bytes = new byte[len];
                new ReadOnlySpan<byte>((void*)q, (int)len).CopyTo(bytes);
                MF.Unlock(b);
                Com.Release(b);
                outputs.Add(bytes);
            }
            finally
            {
                if (o.pSample != own) Com.Release(o.pSample);
                Com.Release(own);
                Com.Release(ownBuffer);
            }
        }
    }

    public void Dispose()
    {
        if (transform != IntPtr.Zero) MF.ProcessMessage(transform, MF.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
        Com.Release(ref transform);
    }

    /// <summary>The NALs of an Annex B access unit.</summary>
    public static List<byte[]> Nals(byte[] annexB)
    {
        var nals = new List<byte[]>();
        int i = 0, start = -1;
        while (i + 3 <= annexB.Length)
        {
            if (annexB[i] == 0 && annexB[i + 1] == 0 && (annexB[i + 2] == 1 || i + 3 < annexB.Length && annexB[i + 2] == 0 && annexB[i + 3] == 1))
            {
                int sc = annexB[i + 2] == 1 ? 3 : 4;
                if (start >= 0) nals.Add(Trim(annexB[start..i]));
                start = i + sc;
                i += sc;
                continue;
            }
            i++;
        }
        if (start >= 0 && start < annexB.Length) nals.Add(annexB[start..]);
        return nals;
    }

    static byte[] Trim(byte[] nal)
    {
        int end = nal.Length;
        while (end > 0 && nal[end - 1] == 0) end--;
        return end == nal.Length ? nal : nal[..end];
    }
}
