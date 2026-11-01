using System.Runtime.InteropServices;

namespace Sill.Windows.Interop;

// Media Foundation, only what the decoder (and the test host's encoder) call. Slots are the vtable
// indices of mfobjects.h, mftransform.h and mfidl.h (10.0.26100.0); IMFAttributes' 33 methods open
// IMFMediaType, IMFSample and IMFActivate.

[StructLayout(LayoutKind.Sequential)]
public struct MFT_REGISTER_TYPE_INFO
{
    public Guid MajorType, Subtype;
}

[StructLayout(LayoutKind.Sequential)]
public struct MFT_INPUT_STREAM_INFO
{
    public long hnsMaxLatency;
    public uint dwFlags, cbSize, cbMaxLookahead, cbAlignment;
}

[StructLayout(LayoutKind.Sequential)]
public struct MFT_OUTPUT_STREAM_INFO
{
    public uint dwFlags, cbSize, cbAlignment;
}

[StructLayout(LayoutKind.Sequential)]
public struct MFT_OUTPUT_DATA_BUFFER
{
    public uint dwStreamID;
    public IntPtr pSample;
    public uint dwStatus;
    public IntPtr pEvents;
}

public static unsafe class MF
{
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_HEVC = new("43564548-0000-0010-8000-00AA00389B71");   // 'HEVC'
    public static readonly Guid MFVideoFormat_NV12 = new("3231564E-0000-0010-8000-00AA00389B71");   // 'NV12'
    public static readonly Guid MFT_CATEGORY_VIDEO_DECODER = new("d6c02d4b-6833-45b4-971a-05a4b04bab91");
    public static readonly Guid MFT_CATEGORY_VIDEO_ENCODER = new("f79eac7d-e545-4387-bdee-d647d7bde42a");

    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MF_MT_VIDEO_NOMINAL_RANGE = new("c21b8ee5-b956-4071-8daf-325edf5cab11");
    public static readonly Guid MF_MT_YUV_MATRIX = new("3e23d450-2c75-4d25-a00e-b91670d12327");
    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new("c9173739-5e56-461c-b713-46fb995cb95f");
    public static readonly Guid MF_LOW_LATENCY = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");   // CODECAPI_AVLowLatencyMode
    public static readonly Guid MF_SA_D3D11_AWARE = new("206b4fc8-fcf9-4c51-afe3-9764369e33a0");
    public static readonly Guid MF_SA_D3D11_BINDFLAGS = new("eacf97ad-065c-4408-bee3-fdcbfd128be2");
    public static readonly Guid MF_TRANSFORM_ASYNC = new("f81a699a-649a-497d-8c73-29f8fed6ad7a");
    public static readonly Guid MF_TRANSFORM_ASYNC_UNLOCK = new("e5666d6b-3422-4eb6-a421-da7db1f8e207");
    public static readonly Guid MFT_FRIENDLY_NAME_Attribute = new("314ffbae-5b41-4c95-9c19-4e7d586face3");
    public static readonly Guid MFT_ENUM_HARDWARE_URL_Attribute = new("2fb866ac-b078-4942-ab6c-003d05cda674");

    public static readonly Guid IID_IMFTransform = new("bf94c121-5b05-4e6f-8000-ba598961414d");
    public static readonly Guid IID_IMFDXGIBuffer = new("e7174cfa-1c9e-48b1-8866-626226bfc258");
    public static readonly Guid IID_IMF2DBuffer = new("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB");
    public static readonly Guid IID_IMFMediaEventGenerator = new("2CD0BD52-BCD5-4B89-B62C-EADC0C031E7D");

    public const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
    public const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);
    public const int MF_E_NOTACCEPTING = unchecked((int)0xC00D36B5);
    public const int MF_E_NO_MORE_TYPES = unchecked((int)0xC00D36B9);
    public const int MF_E_TRANSFORM_TYPE_NOT_SET = unchecked((int)0xC00D6D60);

    public const uint MFT_ENUM_FLAG_SYNCMFT = 0x1, MFT_ENUM_FLAG_ASYNCMFT = 0x2, MFT_ENUM_FLAG_HARDWARE = 0x4,
                      MFT_ENUM_FLAG_LOCALMFT = 0x10, MFT_ENUM_FLAG_SORTANDFILTER = 0x40;
    public const uint MFT_OUTPUT_STREAM_PROVIDES_SAMPLES = 0x100;
    public const uint MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES = 0x200;

    public const uint MFT_MESSAGE_COMMAND_FLUSH = 0, MFT_MESSAGE_COMMAND_DRAIN = 1, MFT_MESSAGE_SET_D3D_MANAGER = 2,
                      MFT_MESSAGE_NOTIFY_BEGIN_STREAMING = 0x10000000, MFT_MESSAGE_NOTIFY_END_STREAMING = 0x10000001,
                      MFT_MESSAGE_NOTIFY_END_OF_STREAM = 0x10000002, MFT_MESSAGE_NOTIFY_START_OF_STREAM = 0x10000003;
    public const uint MFVideoInterlace_Progressive = 2;
    public const uint MFNominalRange_0_255 = 1, MFNominalRange_16_235 = 2;
    public const uint MFVideoTransferMatrix_BT709 = 1, MFVideoTransferMatrix_BT601 = 2;
    public const uint METransformNeedInput = 601, METransformHaveOutput = 602, METransformDrainComplete = 603;

    const uint MF_VERSION = 0x00020070;

    [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] static extern int MFCreateMediaType(IntPtr* type);
    [DllImport("mfplat.dll")] static extern int MFCreateSample(IntPtr* sample);
    [DllImport("mfplat.dll")] static extern int MFCreateMemoryBuffer(uint maxLength, IntPtr* buffer);
    [DllImport("mfplat.dll")] static extern int MFCreateDXGIDeviceManager(uint* resetToken, IntPtr* manager);
    [DllImport("mfplat.dll")] static extern int MFTEnumEx(Guid category, uint flags, MFT_REGISTER_TYPE_INFO* input,
                                                       MFT_REGISTER_TYPE_INFO* output, IntPtr* activates, uint* count);

    static int started;

    public static void Startup()
    {
        if (Interlocked.Exchange(ref started, 1) == 0) Com.Check(MFStartup(MF_VERSION, 0), "MFStartup");
    }

    // IMFAttributes (and everything that starts with it)
    public static void SetUInt32(IntPtr attrs, Guid key, uint value) =>
        Com.Check(((delegate* unmanaged<IntPtr, Guid*, uint, int>)Com.Slot(attrs, 21))(attrs, &key, value), "SetUINT32");

    public static void SetUInt64(IntPtr attrs, Guid key, ulong value) =>
        Com.Check(((delegate* unmanaged<IntPtr, Guid*, ulong, int>)Com.Slot(attrs, 22))(attrs, &key, value), "SetUINT64");

    public static void SetGuid(IntPtr attrs, Guid key, Guid value) =>
        Com.Check(((delegate* unmanaged<IntPtr, Guid*, Guid*, int>)Com.Slot(attrs, 24))(attrs, &key, &value), "SetGUID");

    public static bool TryGetUInt32(IntPtr attrs, Guid key, out uint value)
    {
        uint v = 0;
        int hr = ((delegate* unmanaged<IntPtr, Guid*, uint*, int>)Com.Slot(attrs, 7))(attrs, &key, &v);
        value = v;
        return hr >= 0;
    }

    public static bool TryGetUInt64(IntPtr attrs, Guid key, out ulong value)
    {
        ulong v = 0;
        int hr = ((delegate* unmanaged<IntPtr, Guid*, ulong*, int>)Com.Slot(attrs, 8))(attrs, &key, &v);
        value = v;
        return hr >= 0;
    }

    public static bool TryGetGuid(IntPtr attrs, Guid key, out Guid value)
    {
        Guid v;
        int hr = ((delegate* unmanaged<IntPtr, Guid*, Guid*, int>)Com.Slot(attrs, 10))(attrs, &key, &v);
        value = v;
        return hr >= 0;
    }

    public static string? GetString(IntPtr attrs, Guid key)
    {
        IntPtr s; uint len;
        int hr = ((delegate* unmanaged<IntPtr, Guid*, IntPtr*, uint*, int>)Com.Slot(attrs, 13))(attrs, &key, &s, &len);
        if (hr < 0) return null;
        var text = Marshal.PtrToStringUni(s, (int)len);
        Com.CoTaskMemFree(s);
        return text;
    }

    public static void SetUnknown(IntPtr attrs, Guid key, IntPtr unknown) =>
        Com.Check(((delegate* unmanaged<IntPtr, Guid*, IntPtr, int>)Com.Slot(attrs, 27))(attrs, &key, unknown), "SetUnknown");

    public static IntPtr CreateMediaType()
    {
        IntPtr t;
        Com.Check(MFCreateMediaType(&t), "MFCreateMediaType");
        return t;
    }

    public static IntPtr CreateSample()
    {
        IntPtr s;
        Com.Check(MFCreateSample(&s), "MFCreateSample");
        return s;
    }

    public static IntPtr CreateMemoryBuffer(uint length)
    {
        IntPtr b;
        Com.Check(MFCreateMemoryBuffer(length, &b), "MFCreateMemoryBuffer");
        return b;
    }

    /// <summary>A DXGI device manager holding <paramref name="d3dDevice"/>, for a decoder that decodes on the GPU.</summary>
    public static IntPtr CreateDeviceManager(IntPtr d3dDevice)
    {
        uint token; IntPtr m;
        Com.Check(MFCreateDXGIDeviceManager(&token, &m), "MFCreateDXGIDeviceManager");
        // IMFDXGIDeviceManager::ResetDevice (slot 7)
        int hr = Com.Call(m, 7, d3dDevice, (IntPtr)token);
        if (hr < 0) { Com.Release(m); throw new ComException("IMFDXGIDeviceManager.ResetDevice", hr); }
        return m;
    }

    /// <summary>The transforms registered for <paramref name="category"/> from one subtype to another, best first.</summary>
    public static List<(IntPtr activate, string name, bool hardware, bool async)> Enumerate(Guid category, uint flags, Guid? inSubtype, Guid? outSubtype)
    {
        var input = new MFT_REGISTER_TYPE_INFO { MajorType = MFMediaType_Video, Subtype = inSubtype ?? Guid.Empty };
        var output = new MFT_REGISTER_TYPE_INFO { MajorType = MFMediaType_Video, Subtype = outSubtype ?? Guid.Empty };
        IntPtr array; uint count;
        Com.Check(MFTEnumEx(category, flags, inSubtype.HasValue ? &input : null, outSubtype.HasValue ? &output : null, &array, &count), "MFTEnumEx");
        var list = new List<(IntPtr, string, bool, bool)>();
        for (int i = 0; i < count; i++)
        {
            var a = ((IntPtr*)array)[i];
            var name = GetString(a, MFT_FRIENDLY_NAME_Attribute) ?? "(unnamed)";
            bool hw = GetString(a, MFT_ENUM_HARDWARE_URL_Attribute) != null;
            bool async = TryGetUInt32(a, MF_TRANSFORM_ASYNC, out var v) && v != 0;
            list.Add((a, name, hw, async));
        }
        Com.CoTaskMemFree(array);
        return list;
    }

    /// <summary>IMFActivate::ActivateObject: the transform itself.</summary>
    public static IntPtr Activate(IntPtr activate)
    {
        var iid = IID_IMFTransform;
        IntPtr t;
        Com.Check(Com.Call(activate, 33, (IntPtr)(&iid), (IntPtr)(&t)), "IMFActivate.ActivateObject");
        return t;
    }

    // IMFTransform
    public static IntPtr GetAttributes(IntPtr transform)
    {
        IntPtr a;
        int hr = Com.Call(transform, 8, (IntPtr)(&a));
        return hr >= 0 ? a : IntPtr.Zero;
    }

    public static IntPtr GetOutputStreamAttributes(IntPtr transform)
    {
        IntPtr a;
        int hr = Com.Call(transform, 10, IntPtr.Zero, (IntPtr)(&a));
        return hr >= 0 ? a : IntPtr.Zero;
    }

    public static MFT_INPUT_STREAM_INFO GetInputStreamInfo(IntPtr transform)
    {
        MFT_INPUT_STREAM_INFO i;
        Com.Check(Com.Call(transform, 6, IntPtr.Zero, (IntPtr)(&i)), "GetInputStreamInfo");
        return i;
    }

    public static MFT_OUTPUT_STREAM_INFO GetOutputStreamInfo(IntPtr transform)
    {
        MFT_OUTPUT_STREAM_INFO i;
        Com.Check(Com.Call(transform, 7, IntPtr.Zero, (IntPtr)(&i)), "GetOutputStreamInfo");
        return i;
    }

    public static int GetInputAvailableType(IntPtr transform, uint index, out IntPtr type)
    {
        IntPtr t;
        int hr = Com.Call(transform, 13, IntPtr.Zero, (IntPtr)index, (IntPtr)(&t));
        type = hr >= 0 ? t : IntPtr.Zero;
        return hr;
    }

    public static int GetOutputAvailableType(IntPtr transform, uint index, out IntPtr type)
    {
        IntPtr t;
        int hr = Com.Call(transform, 14, IntPtr.Zero, (IntPtr)index, (IntPtr)(&t));
        type = hr >= 0 ? t : IntPtr.Zero;
        return hr;
    }

    public static int SetInputType(IntPtr transform, IntPtr type) => Com.Call(transform, 15, IntPtr.Zero, type, IntPtr.Zero);
    public static int SetOutputType(IntPtr transform, IntPtr type) => Com.Call(transform, 16, IntPtr.Zero, type, IntPtr.Zero);

    public static IntPtr GetOutputCurrentType(IntPtr transform)
    {
        IntPtr t;
        Com.Check(Com.Call(transform, 18, IntPtr.Zero, (IntPtr)(&t)), "GetOutputCurrentType");
        return t;
    }

    public static int ProcessMessage(IntPtr transform, uint message, IntPtr param) => Com.Call(transform, 23, (IntPtr)message, param);
    public static int ProcessInput(IntPtr transform, IntPtr sample) => Com.Call(transform, 24, IntPtr.Zero, sample, IntPtr.Zero);

    public static int ProcessOutput(IntPtr transform, ref MFT_OUTPUT_DATA_BUFFER buffer, out uint status)
    {
        uint s;
        int hr;
        fixed (MFT_OUTPUT_DATA_BUFFER* b = &buffer)
            hr = Com.Call(transform, 25, IntPtr.Zero, (IntPtr)1, (IntPtr)b, (IntPtr)(&s));
        status = s;
        return hr;
    }

    // IMFSample
    public static void SetSampleTime(IntPtr sample, long hns) => Com.Check(Com.Call(sample, 36, (IntPtr)hns), "SetSampleTime");
    public static void SetSampleDuration(IntPtr sample, long hns) => Com.Check(Com.Call(sample, 38, (IntPtr)hns), "SetSampleDuration");
    public static void AddBuffer(IntPtr sample, IntPtr buffer) => Com.Check(Com.Call(sample, 42, buffer), "AddBuffer");

    public static long GetSampleTime(IntPtr sample)
    {
        long t;
        return Com.Call(sample, 35, (IntPtr)(&t)) >= 0 ? t : 0;
    }

    public static IntPtr GetBufferByIndex(IntPtr sample, uint index)
    {
        IntPtr b;
        Com.Check(Com.Call(sample, 40, (IntPtr)index, (IntPtr)(&b)), "GetBufferByIndex");
        return b;
    }

    public static IntPtr ConvertToContiguousBuffer(IntPtr sample)
    {
        IntPtr b;
        Com.Check(Com.Call(sample, 41, (IntPtr)(&b)), "ConvertToContiguousBuffer");
        return b;
    }

    public static uint GetSampleUInt32(IntPtr sample, Guid key) => TryGetUInt32(sample, key, out var v) ? v : 0;

    // IMFMediaBuffer
    public static IntPtr Lock(IntPtr buffer, out uint maxLength, out uint currentLength)
    {
        IntPtr p; uint max, cur;
        Com.Check(Com.Call(buffer, 3, (IntPtr)(&p), (IntPtr)(&max), (IntPtr)(&cur)), "IMFMediaBuffer.Lock");
        maxLength = max; currentLength = cur;
        return p;
    }

    public static void Unlock(IntPtr buffer) => Com.Call(buffer, 4);
    public static void SetCurrentLength(IntPtr buffer, uint length) => Com.Check(Com.Call(buffer, 6, (IntPtr)length), "SetCurrentLength");

    // IMF2DBuffer
    public static IntPtr Lock2D(IntPtr buffer2D, out int pitch)
    {
        IntPtr p; int pt;
        Com.Check(Com.Call(buffer2D, 3, (IntPtr)(&p), (IntPtr)(&pt)), "IMF2DBuffer.Lock2D");
        pitch = pt;
        return p;
    }

    public static void Unlock2D(IntPtr buffer2D) => Com.Call(buffer2D, 4);

    // IMFDXGIBuffer: the texture a GPU decoder's output sample holds, and which slice of it.
    public static (IntPtr texture, uint subresource) GetTexture(IntPtr dxgiBuffer)
    {
        var iid = D3D.IID_ID3D11Texture2D;
        IntPtr t; uint sub;
        Com.Check(Com.Call(dxgiBuffer, 3, (IntPtr)(&iid), (IntPtr)(&t)), "IMFDXGIBuffer.GetResource");
        Com.Check(Com.Call(dxgiBuffer, 4, (IntPtr)(&sub)), "IMFDXGIBuffer.GetSubresourceIndex");
        return (t, sub);
    }

    // IMFMediaEventGenerator, for an asynchronous (hardware) transform: the test host's encoder.
    public static int GetEvent(IntPtr generator, bool noWait, out IntPtr ev)
    {
        IntPtr e;
        int hr = Com.Call(generator, 3, (IntPtr)(noWait ? 1 : 0), (IntPtr)(&e));
        ev = hr >= 0 ? e : IntPtr.Zero;
        return hr;
    }

    public static uint EventType(IntPtr ev)
    {
        uint t;
        Com.Check(Com.Call(ev, 33, (IntPtr)(&t)), "IMFMediaEvent.GetType");
        return t;
    }

    public static ulong Pack(uint hi, uint lo) => ((ulong)hi << 32) | lo;
    public static (uint hi, uint lo) Unpack(ulong v) => ((uint)(v >> 32), (uint)v);
}
