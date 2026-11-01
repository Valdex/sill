using System.Runtime.InteropServices;
using Sill.Windows.Interop;

namespace Sill.Windows.Video;

/// <summary>One decoded picture. A GPU decoder's is a slice of its texture, valid only during the
/// callback; a software decoder's is NV12 in memory, also valid only during the callback.</summary>
public readonly struct DecodedFrame
{
    public IntPtr Texture { get; init; }          // ID3D11Texture2D (NV12), GPU path
    public uint Subresource { get; init; }
    public IntPtr Data { get; init; }             // NV12 bytes, software path
    public int Pitch { get; init; }
    public int Width { get; init; }               // the decoder's surface (often aligned past the picture)
    public int Height { get; init; }
    public bool FullRange { get; init; }
    public bool Bt601 { get; init; }
}

/// <summary>Media Foundation's HEVC decoder (the HEVC Video Extensions' on Windows 11), fed Annex B
/// access units one at a time, in low-latency mode (a picture out for every one in, no reordering:
/// the Mac encodes without B-frames). On the GPU through a DXGI device manager when the decoder is
/// D3D11-aware, else in software. Used from one thread only.</summary>
public sealed unsafe class HevcDecoder : IDisposable
{
    IntPtr transform, deviceManager, outputType;
    readonly bool gpu;
    bool providesSamples;
    uint outputSize;
    int width, height, stride;
    bool fullRange = true, bt601;
    long sampleTime;

    public string Name { get; }
    public bool OnGpu => gpu;

    public static string? Problem;

    HevcDecoder(IntPtr transform, string name, IntPtr d3dDevice)
    {
        this.transform = transform;
        Name = name;
        var attrs = MF.GetAttributes(transform);
        if (attrs != IntPtr.Zero)
        {
            try { MF.SetUInt32(attrs, MF.MF_LOW_LATENCY, 1); } catch (ComException) { }
            bool aware = MF.TryGetUInt32(attrs, MF.MF_SA_D3D11_AWARE, out var a) && a != 0;
            if (d3dDevice != IntPtr.Zero && aware)
            {
                try
                {
                    deviceManager = MF.CreateDeviceManager(d3dDevice);
                    Com.Check(MF.ProcessMessage(transform, MF.MFT_MESSAGE_SET_D3D_MANAGER, deviceManager), "SET_D3D_MANAGER");
                    gpu = true;
                }
                catch (ComException)
                {
                    MF.ProcessMessage(transform, MF.MFT_MESSAGE_SET_D3D_MANAGER, IntPtr.Zero);
                    Com.Release(ref deviceManager);
                    gpu = false;
                }
            }
            Com.Release(attrs);
        }
    }

    /// <summary>A decoder for pictures of about this size, on <paramref name="d3dDevice"/>'s GPU when
    /// it can (zero: software). Null when Windows has no HEVC decoder (the HEVC Video Extensions are
    /// missing): <see cref="Problem"/> says so.</summary>
    public static HevcDecoder? Create(IntPtr d3dDevice, int width, int height)
    {
        MF.Startup();
        var found = MF.Enumerate(MF.MFT_CATEGORY_VIDEO_DECODER, MF.MFT_ENUM_FLAG_SYNCMFT | MF.MFT_ENUM_FLAG_LOCALMFT | MF.MFT_ENUM_FLAG_SORTANDFILTER,
                                 MF.MFVideoFormat_HEVC, MF.MFVideoFormat_NV12);
        HevcDecoder? decoder = null;
        foreach (var (activate, name, _, _) in found)
        {
            if (decoder == null)
            {
                try
                {
                    var t = MF.Activate(activate);
                    var d = new HevcDecoder(t, name, d3dDevice);
                    if (d.Configure(width, height)) decoder = d; else d.Dispose();
                }
                catch (ComException e) { Problem = $"{name}: {e.Message}"; }
            }
            Com.Release(activate);
        }
        if (decoder == null && found.Count == 0)
            Problem = "Windows has no HEVC decoder. Install “HEVC Video Extensions” from the Microsoft Store.";
        return decoder;
    }

    bool Configure(int w, int h)
    {
        var input = MF.CreateMediaType();
        try
        {
            MF.SetGuid(input, MF.MF_MT_MAJOR_TYPE, MF.MFMediaType_Video);
            MF.SetGuid(input, MF.MF_MT_SUBTYPE, MF.MFVideoFormat_HEVC);
            if (w > 0 && h > 0) MF.SetUInt64(input, MF.MF_MT_FRAME_SIZE, MF.Pack((uint)w, (uint)h));
            MF.SetUInt32(input, MF.MF_MT_INTERLACE_MODE, MF.MFVideoInterlace_Progressive);
            MF.SetUInt64(input, MF.MF_MT_FRAME_RATE, MF.Pack(60, 1));
            int hr = MF.SetInputType(transform, input);
            if (hr < 0) { Problem = $"{Name} refused HEVC input: 0x{hr:X8}"; return false; }
        }
        finally { Com.Release(input); }
        if (!ChooseOutput()) return false;
        MF.ProcessMessage(transform, MF.MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, IntPtr.Zero);
        MF.ProcessMessage(transform, MF.MFT_MESSAGE_NOTIFY_START_OF_STREAM, IntPtr.Zero);
        return true;
    }

    /// <summary>NV12 among the output types the decoder offers now (at start, and after a stream change).</summary>
    bool ChooseOutput()
    {
        for (uint i = 0; ; i++)
        {
            int hr = MF.GetOutputAvailableType(transform, i, out var type);
            if (hr < 0) { Problem = $"{Name} offers no NV12 output"; return false; }
            bool nv12 = MF.TryGetGuid(type, MF.MF_MT_SUBTYPE, out var sub) && sub == MF.MFVideoFormat_NV12;
            if (nv12 && MF.SetOutputType(transform, type) >= 0)
            {
                Com.Release(ref outputType);
                outputType = type;
                ReadOutputType();
                return true;
            }
            Com.Release(type);
        }
    }

    void ReadOutputType()
    {
        if (MF.TryGetUInt64(outputType, MF.MF_MT_FRAME_SIZE, out var size)) { width = (int)(size >> 32); height = (int)(uint)size; }
        stride = MF.TryGetUInt32(outputType, MF.MF_MT_DEFAULT_STRIDE, out var s) ? (int)s : width;
        // The Mac captures full-range BT.709 (WindowCapture: 420f, sRGB); the decoder says what the
        // stream's VUI says when it says anything.
        fullRange = !MF.TryGetUInt32(outputType, MF.MF_MT_VIDEO_NOMINAL_RANGE, out var range) || range != MF.MFNominalRange_16_235;
        bt601 = MF.TryGetUInt32(outputType, MF.MF_MT_YUV_MATRIX, out var matrix) && matrix == MF.MFVideoTransferMatrix_BT601;
        var info = MF.GetOutputStreamInfo(transform);
        providesSamples = (info.dwFlags & MF.MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) != 0;
        outputSize = info.cbSize;
    }

    /// <summary>One access unit in; every picture it completes goes to <paramref name="onFrame"/>. False
    /// when the decoder failed (the caller waits for the next keyframe and makes a new one).</summary>
    public bool Decode(ReadOnlySpan<byte> annexB, Action<DecodedFrame> onFrame)
    {
        var buffer = MF.CreateMemoryBuffer((uint)annexB.Length);
        var sample = MF.CreateSample();
        try
        {
            var p = MF.Lock(buffer, out _, out _);
            annexB.CopyTo(new Span<byte>((void*)p, annexB.Length));
            MF.Unlock(buffer);
            MF.SetCurrentLength(buffer, (uint)annexB.Length);
            MF.AddBuffer(sample, buffer);
            MF.SetSampleTime(sample, sampleTime);
            MF.SetSampleDuration(sample, 166_667);
            sampleTime += 166_667;
            int hr = MF.ProcessInput(transform, sample);
            if (hr == MF.MF_E_NOTACCEPTING)
            {
                if (!Drain(onFrame)) return false;
                hr = MF.ProcessInput(transform, sample);
            }
            if (hr < 0) { Problem = $"ProcessInput: 0x{hr:X8}"; return false; }
            return Drain(onFrame);
        }
        finally
        {
            Com.Release(sample);
            Com.Release(buffer);
        }
    }

    bool Drain(Action<DecodedFrame> onFrame)
    {
        for (int guard = 0; guard < 16; guard++)
        {
            IntPtr own = IntPtr.Zero, ownBuffer = IntPtr.Zero;
            if (!providesSamples)
            {
                own = MF.CreateSample();
                ownBuffer = MF.CreateMemoryBuffer(Math.Max(outputSize, (uint)(Math.Max(stride, width) * height * 3 / 2)));
                MF.AddBuffer(own, ownBuffer);
            }
            var output = new MFT_OUTPUT_DATA_BUFFER { dwStreamID = 0, pSample = own };
            int hr = MF.ProcessOutput(transform, ref output, out _);
            Com.Release(output.pEvents);
            try
            {
                if (hr == MF.MF_E_TRANSFORM_NEED_MORE_INPUT) return true;
                if (hr == MF.MF_E_TRANSFORM_STREAM_CHANGE)
                {
                    if (!ChooseOutput()) return false;
                    continue;
                }
                if (hr < 0) { Problem = $"ProcessOutput: 0x{hr:X8}"; return false; }
                if (output.pSample != IntPtr.Zero) Deliver(output.pSample, onFrame);
            }
            finally
            {
                if (output.pSample != own) Com.Release(output.pSample);
                Com.Release(own);
                Com.Release(ownBuffer);
            }
        }
        return true;
    }

    void Deliver(IntPtr sample, Action<DecodedFrame> onFrame)
    {
        var buffer = MF.GetBufferByIndex(sample, 0);
        try
        {
            if (gpu && Com.QueryInterface(buffer, MF.IID_IMFDXGIBuffer, out var dxgi) >= 0)
            {
                try
                {
                    var (texture, sub) = MF.GetTexture(dxgi);
                    try
                    {
                        var desc = D3D.GetDesc(texture);
                        onFrame(new DecodedFrame { Texture = texture, Subresource = sub, Width = (int)desc.Width, Height = (int)desc.Height, FullRange = fullRange, Bt601 = bt601 });
                    }
                    finally { Com.Release(texture); }
                }
                finally { Com.Release(dxgi); }
                return;
            }
            if (Com.QueryInterface(buffer, MF.IID_IMF2DBuffer, out var b2) >= 0)
            {
                try
                {
                    var p = MF.Lock2D(b2, out int pitch);
                    try { onFrame(new DecodedFrame { Data = p, Pitch = pitch, Width = width, Height = height, FullRange = fullRange, Bt601 = bt601 }); }
                    finally { MF.Unlock2D(b2); }
                }
                finally { Com.Release(b2); }
                return;
            }
            var q = MF.Lock(buffer, out _, out _);
            try { onFrame(new DecodedFrame { Data = q, Pitch = stride > 0 ? stride : width, Width = width, Height = height, FullRange = fullRange, Bt601 = bt601 }); }
            finally { MF.Unlock(buffer); }
        }
        finally { Com.Release(buffer); }
    }

    public void Dispose()
    {
        if (transform != IntPtr.Zero)
        {
            MF.ProcessMessage(transform, MF.MFT_MESSAGE_NOTIFY_END_STREAMING, IntPtr.Zero);
            if (gpu) MF.ProcessMessage(transform, MF.MFT_MESSAGE_SET_D3D_MANAGER, IntPtr.Zero);
        }
        Com.Release(ref outputType);
        Com.Release(ref transform);
        Com.Release(ref deviceManager);
    }
}
