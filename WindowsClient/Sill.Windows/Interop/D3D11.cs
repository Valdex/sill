using System.Runtime.InteropServices;

namespace Sill.Windows.Interop;

// Direct3D 11 and DXGI, only the calls the renderer and the decoder use. Slots are the vtable
// indices of d3d11.h, dxgi.h, dxgi1_2.h and dxgi1_3.h (10.0.26100.0), IUnknown's three counted.

[StructLayout(LayoutKind.Sequential)]
public struct D3D11_TEXTURE2D_DESC
{
    public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct D3D11_SHADER_RESOURCE_VIEW_DESC
{
    public uint Format, ViewDimension;
    // The union's Texture2D member (MostDetailedMip, MipLevels), padded to the union's 16 bytes.
    public uint MostDetailedMip, MipLevels, Pad0, Pad1;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct D3D11_SAMPLER_DESC
{
    public uint Filter, AddressU, AddressV, AddressW;
    public float MipLODBias;
    public uint MaxAnisotropy, ComparisonFunc;
    public fixed float BorderColor[4];
    public float MinLOD, MaxLOD;
}

[StructLayout(LayoutKind.Sequential)]
public struct D3D11_BUFFER_DESC
{
    public uint ByteWidth, Usage, BindFlags, CPUAccessFlags, MiscFlags, StructureByteStride;
}

[StructLayout(LayoutKind.Sequential)]
public struct D3D11_VIEWPORT
{
    public float TopLeftX, TopLeftY, Width, Height, MinDepth, MaxDepth;
}

[StructLayout(LayoutKind.Sequential)]
public struct D3D11_MAPPED_SUBRESOURCE
{
    public IntPtr pData;
    public uint RowPitch, DepthPitch;
}

[StructLayout(LayoutKind.Sequential)]
public struct DXGI_SWAP_CHAIN_DESC1
{
    public uint Width, Height, Format, Stereo, SampleCount, SampleQuality, BufferUsage, BufferCount, Scaling, SwapEffect, AlphaMode, Flags;
}

public static unsafe class D3D
{
    public static readonly Guid IID_ID3D11Device = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");
    public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    public static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IID_IDXGIFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    public static readonly Guid IID_IDXGISwapChain2 = new("a8be2ac4-199f-4946-b331-79599fb98de7");
    public static readonly Guid IID_ID3D10Multithread = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");

    public const uint DXGI_FORMAT_R8G8_UNORM = 49;
    public const uint DXGI_FORMAT_R8_UNORM = 61;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint DXGI_FORMAT_NV12 = 103;

    public const uint D3D11_BIND_CONSTANT_BUFFER = 0x4;
    public const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET = 0x20;
    public const uint D3D11_USAGE_DEFAULT = 0;
    public const uint D3D11_USAGE_DYNAMIC = 2;
    public const uint D3D11_USAGE_STAGING = 3;
    public const uint D3D11_CPU_ACCESS_WRITE = 0x10000;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const uint D3D11_MAP_READ = 1;
    public const uint D3D11_MAP_WRITE_DISCARD = 4;
    public const uint D3D11_SRV_DIMENSION_TEXTURE2D = 4;
    public const uint D3D11_FILTER_MIN_MAG_MIP_LINEAR = 0x15;
    public const uint D3D11_TEXTURE_ADDRESS_CLAMP = 3;
    public const uint D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST = 4;

    public const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x20;
    public const uint DXGI_SWAP_EFFECT_FLIP_DISCARD = 4;
    public const uint DXGI_SCALING_STRETCH = 0;
    public const uint DXGI_ALPHA_MODE_IGNORE = 3;
    public const uint DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT = 0x40;
    public const uint DXGI_MWA_NO_ALT_ENTER = 2;

    const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    const uint D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800;
    const uint D3D11_SDK_VERSION = 7;

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags, uint* featureLevels,
                                        uint levels, uint sdkVersion, IntPtr* device, uint* featureLevel, IntPtr* context);

    [DllImport("d3dcompiler_47.dll")]
    static extern int D3DCompile(byte* src, nuint size, byte* name, IntPtr defines, IntPtr include, byte* entry, byte* target,
                                 uint flags1, uint flags2, IntPtr* code, IntPtr* errors);

    /// <summary>A hardware device for decoding and drawing, multithread protected (Media Foundation's
    /// decoder uses it from its own threads). Video support first; without it (an old driver) a
    /// plain device, and the decoder then runs in software.</summary>
    public static (IntPtr device, IntPtr context, bool video) CreateDevice()
    {
        uint* levels = stackalloc uint[] { 0xb100, 0xb000, 0xa100, 0xa000 };
        foreach (var flags in new[] { D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT, D3D11_CREATE_DEVICE_BGRA_SUPPORT })
        {
            IntPtr dev, ctx;
            uint level;
            int hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, flags, levels, 4, D3D11_SDK_VERSION, &dev, &level, &ctx);
            if (hr < 0) continue;
            if (Com.QueryInterface(dev, IID_ID3D10Multithread, out var mt) >= 0)
            {
                // ID3D10Multithread::SetMultithreadProtected (slot 5)
                ((delegate* unmanaged<IntPtr, int, int>)Com.Slot(mt, 5))(mt, 1);
                Com.Release(mt);
            }
            return (dev, ctx, (flags & D3D11_CREATE_DEVICE_VIDEO_SUPPORT) != 0);
        }
        throw new ComException("D3D11CreateDevice", unchecked((int)0x887A0004));
    }

    // ID3D11Device
    public static IntPtr CreateTexture2D(IntPtr device, in D3D11_TEXTURE2D_DESC desc)
    {
        IntPtr tex;
        fixed (D3D11_TEXTURE2D_DESC* d = &desc)
            Com.Check(Com.Call(device, 5, (IntPtr)d, IntPtr.Zero, (IntPtr)(&tex)), "CreateTexture2D");
        return tex;
    }

    public static IntPtr CreateShaderResourceView(IntPtr device, IntPtr resource, uint format)
    {
        var desc = new D3D11_SHADER_RESOURCE_VIEW_DESC { Format = format, ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D, MostDetailedMip = 0, MipLevels = 1 };
        IntPtr srv;
        Com.Check(Com.Call(device, 7, resource, (IntPtr)(&desc), (IntPtr)(&srv)), "CreateShaderResourceView");
        return srv;
    }

    public static IntPtr CreateRenderTargetView(IntPtr device, IntPtr resource)
    {
        IntPtr rtv;
        Com.Check(Com.Call(device, 9, resource, IntPtr.Zero, (IntPtr)(&rtv)), "CreateRenderTargetView");
        return rtv;
    }

    public static IntPtr CreateVertexShader(IntPtr device, byte[] code)
    {
        IntPtr vs;
        fixed (byte* p = code)
            Com.Check(Com.Call(device, 12, (IntPtr)p, (IntPtr)code.Length, IntPtr.Zero, (IntPtr)(&vs)), "CreateVertexShader");
        return vs;
    }

    public static IntPtr CreatePixelShader(IntPtr device, byte[] code)
    {
        IntPtr ps;
        fixed (byte* p = code)
            Com.Check(Com.Call(device, 15, (IntPtr)p, (IntPtr)code.Length, IntPtr.Zero, (IntPtr)(&ps)), "CreatePixelShader");
        return ps;
    }

    public static IntPtr CreateLinearSampler(IntPtr device)
    {
        var d = new D3D11_SAMPLER_DESC
        {
            Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR, AddressU = D3D11_TEXTURE_ADDRESS_CLAMP, AddressV = D3D11_TEXTURE_ADDRESS_CLAMP,
            AddressW = D3D11_TEXTURE_ADDRESS_CLAMP, MaxAnisotropy = 1, ComparisonFunc = 1 /* NEVER */, MinLOD = 0, MaxLOD = float.MaxValue,
        };
        IntPtr s;
        Com.Check(Com.Call(device, 23, (IntPtr)(&d), (IntPtr)(&s)), "CreateSamplerState");
        return s;
    }

    public static IntPtr CreateDynamicConstantBuffer(IntPtr device, uint bytes)
    {
        var d = new D3D11_BUFFER_DESC { ByteWidth = bytes, Usage = D3D11_USAGE_DYNAMIC, BindFlags = D3D11_BIND_CONSTANT_BUFFER, CPUAccessFlags = D3D11_CPU_ACCESS_WRITE };
        IntPtr b;
        Com.Check(Com.Call(device, 3, (IntPtr)(&d), IntPtr.Zero, (IntPtr)(&b)), "CreateBuffer");
        return b;
    }

    // ID3D11DeviceContext
    public static void PSSetShaderResources(IntPtr ctx, IntPtr srv0, IntPtr srv1)
    {
        IntPtr* v = stackalloc IntPtr[2] { srv0, srv1 };
        Com.Call(ctx, 8, IntPtr.Zero, (IntPtr)2, (IntPtr)v);
    }

    public static void PSSetShader(IntPtr ctx, IntPtr ps) => Com.Call(ctx, 9, ps, IntPtr.Zero, IntPtr.Zero);
    public static void VSSetShader(IntPtr ctx, IntPtr vs) => Com.Call(ctx, 11, vs, IntPtr.Zero, IntPtr.Zero);

    public static void PSSetSampler(IntPtr ctx, IntPtr sampler)
    {
        IntPtr s = sampler;
        Com.Call(ctx, 10, IntPtr.Zero, (IntPtr)1, (IntPtr)(&s));
    }

    public static void PSSetConstantBuffer(IntPtr ctx, IntPtr buffer)
    {
        IntPtr b = buffer;
        Com.Call(ctx, 16, IntPtr.Zero, (IntPtr)1, (IntPtr)(&b));
    }

    public static void Draw(IntPtr ctx, uint vertices) => Com.Call(ctx, 13, (IntPtr)vertices, IntPtr.Zero);

    public static D3D11_MAPPED_SUBRESOURCE Map(IntPtr ctx, IntPtr resource, uint mapType)
    {
        D3D11_MAPPED_SUBRESOURCE m;
        Com.Check(Com.Call(ctx, 14, resource, IntPtr.Zero, (IntPtr)mapType, IntPtr.Zero, (IntPtr)(&m)), "Map");
        return m;
    }

    public static void Unmap(IntPtr ctx, IntPtr resource) => Com.Call(ctx, 15, resource, IntPtr.Zero);

    public static void IASetPrimitiveTopology(IntPtr ctx, uint topology) => Com.Call(ctx, 24, (IntPtr)topology);
    public static void IASetInputLayout(IntPtr ctx, IntPtr layout) => Com.Call(ctx, 17, layout);

    public static void OMSetRenderTarget(IntPtr ctx, IntPtr rtv)
    {
        IntPtr r = rtv;
        Com.Call(ctx, 33, (IntPtr)(rtv == IntPtr.Zero ? 0 : 1), rtv == IntPtr.Zero ? IntPtr.Zero : (IntPtr)(&r), IntPtr.Zero);
    }

    public static void RSSetViewport(IntPtr ctx, D3D11_VIEWPORT vp) => Com.Call(ctx, 44, (IntPtr)1, (IntPtr)(&vp));

    public static void CopySubresourceRegion(IntPtr ctx, IntPtr dst, uint dstSub, IntPtr src, uint srcSub)
    {
        // (dst, DstSubresource, DstX, DstY, DstZ, src, SrcSubresource, pSrcBox): nine arguments.
        ((delegate* unmanaged<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, IntPtr, void>)Com.Slot(ctx, 46))(ctx, dst, dstSub, 0, 0, 0, src, srcSub, IntPtr.Zero);
    }

    public static void UpdateSubresource(IntPtr ctx, IntPtr dst, IntPtr data, uint rowPitch)
    {
        ((delegate* unmanaged<IntPtr, IntPtr, uint, IntPtr, IntPtr, uint, uint, void>)Com.Slot(ctx, 48))(ctx, dst, 0, IntPtr.Zero, data, rowPitch, 0);
    }

    public static void ClearRenderTargetView(IntPtr ctx, IntPtr rtv, float r, float g, float b)
    {
        float* c = stackalloc float[4] { r, g, b, 1 };
        Com.Call(ctx, 50, rtv, (IntPtr)c);
    }

    public static void ClearState(IntPtr ctx) => Com.CallVoid(ctx, 110);
    public static void Flush(IntPtr ctx) => Com.CallVoid(ctx, 111);

    public static D3D11_TEXTURE2D_DESC GetDesc(IntPtr texture2D)
    {
        D3D11_TEXTURE2D_DESC d;
        Com.Call(texture2D, 10, (IntPtr)(&d));
        return d;
    }

    // DXGI
    /// <summary>The factory of the adapter the device runs on: the one its swap chains must come from.</summary>
    public static IntPtr FactoryOf(IntPtr device)
    {
        var dxgiDevice = Com.Query(device, IID_IDXGIDevice, "IDXGIDevice");
        try
        {
            IntPtr adapter;
            Com.Check(Com.Call(dxgiDevice, 7, (IntPtr)(&adapter)), "IDXGIDevice.GetAdapter");
            try
            {
                IntPtr factory;
                var iid = IID_IDXGIFactory2;
                Com.Check(Com.Call(adapter, 6, (IntPtr)(&iid), (IntPtr)(&factory)), "IDXGIAdapter.GetParent");
                return factory;
            }
            finally { Com.Release(adapter); }
        }
        finally { Com.Release(dxgiDevice); }
    }

    public static IntPtr CreateSwapChainForHwnd(IntPtr factory, IntPtr device, IntPtr hwnd, in DXGI_SWAP_CHAIN_DESC1 desc)
    {
        IntPtr sc;
        fixed (DXGI_SWAP_CHAIN_DESC1* d = &desc)
            Com.Check(Com.Call(factory, 15, device, hwnd, (IntPtr)d, IntPtr.Zero, IntPtr.Zero, (IntPtr)(&sc)), "CreateSwapChainForHwnd");
        // IDXGIFactory::MakeWindowAssociation: no Alt+Enter full screen of DXGI's own.
        Com.Call(factory, 8, hwnd, (IntPtr)DXGI_MWA_NO_ALT_ENTER);
        return sc;
    }

    public static int Present(IntPtr swapChain, uint syncInterval, uint flags) => Com.Call(swapChain, 8, (IntPtr)syncInterval, (IntPtr)flags);

    public static IntPtr GetBuffer(IntPtr swapChain)
    {
        IntPtr buf;
        var iid = IID_ID3D11Texture2D;
        Com.Check(Com.Call(swapChain, 9, IntPtr.Zero, (IntPtr)(&iid), (IntPtr)(&buf)), "IDXGISwapChain.GetBuffer");
        return buf;
    }

    public static int ResizeBuffers(IntPtr swapChain, uint count, uint width, uint height, uint format, uint flags) =>
        Com.Call(swapChain, 13, (IntPtr)count, (IntPtr)width, (IntPtr)height, (IntPtr)format, (IntPtr)flags);

    /// <summary>IDXGISwapChain2: at most one frame queued, and the object that says when the next may be drawn.</summary>
    public static IntPtr LowLatency(IntPtr swapChain)
    {
        if (Com.QueryInterface(swapChain, IID_IDXGISwapChain2, out var sc2) < 0) return IntPtr.Zero;
        try
        {
            Com.Call(sc2, 31, (IntPtr)1);                                  // SetMaximumFrameLatency
            return ((delegate* unmanaged<IntPtr, IntPtr>)Com.Slot(sc2, 33))(sc2);   // GetFrameLatencyWaitableObject
        }
        finally { Com.Release(sc2); }
    }

    // Shaders, compiled at start by the system's compiler (d3dcompiler_47.dll ships with Windows).
    public static byte[] Compile(string source, string entry, string target)
    {
        var src = System.Text.Encoding.UTF8.GetBytes(source);
        var e = System.Text.Encoding.ASCII.GetBytes(entry + "\0");
        var t = System.Text.Encoding.ASCII.GetBytes(target + "\0");
        var n = System.Text.Encoding.ASCII.GetBytes("sill.hlsl\0");
        IntPtr code, errors;
        int hr;
        fixed (byte* ps = src, pe = e, pt = t, pn = n)
            hr = D3DCompile(ps, (nuint)src.Length, pn, IntPtr.Zero, IntPtr.Zero, pe, pt, 1u << 15 /* OPTIMIZATION_LEVEL3 */, 0, &code, &errors);
        if (hr < 0)
        {
            string msg = errors != IntPtr.Zero ? Marshal.PtrToStringAnsi(BlobPointer(errors), (int)BlobSize(errors)) : "";
            Com.Release(errors);
            throw new InvalidOperationException($"Shader {entry} did not compile: 0x{hr:X8} {msg}");
        }
        Com.Release(errors);
        var bytes = new byte[(int)BlobSize(code)];
        Marshal.Copy(BlobPointer(code), bytes, 0, bytes.Length);
        Com.Release(code);
        return bytes;
    }

    static IntPtr BlobPointer(IntPtr blob) => ((delegate* unmanaged<IntPtr, IntPtr>)Com.Slot(blob, 3))(blob);
    static nuint BlobSize(IntPtr blob) => ((delegate* unmanaged<IntPtr, nuint>)Com.Slot(blob, 4))(blob);
}
