using System.Runtime.InteropServices;
using Sill.Windows.Interop;

namespace Sill.Windows.Video;

/// <summary>The GPU went away under the renderer (a driver updated or reset, a TDR, the adapter
/// removed): DXGI says so at Present and ResizeBuffers. The video thread makes its device, renderer
/// and decoder again, and the picture waits for a keyframe.</summary>
public sealed class DeviceLostException : Exception
{
    public const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005), DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
    public int HResult32 { get; }
    public DeviceLostException(int hr) : base($"The Direct3D device was lost (0x{hr:X8}).") { HResult32 = hr; HResult = hr; }

    public static void ThrowIfLost(int hr)
    {
        if (hr is DXGI_ERROR_DEVICE_REMOVED or DXGI_ERROR_DEVICE_RESET) throw new DeviceLostException(hr);
    }
}

/// <summary>Draws the decoded picture into a window with Direct3D 11: a flip-model swap chain with
/// one frame of latency, NV12 to RGB in a pixel shader (BT.709 full range, as the Mac captures),
/// fitted to the window with black bars, and supersampled when it is shown smaller than it is (a
/// Retina frame on a smaller window), so text stays readable. One thread only.</summary>
public sealed unsafe class Renderer : IDisposable
{
    const string Shader = @"
Texture2D<float> texY : register(t0);
Texture2D<float2> texUV : register(t1);
SamplerState samp : register(s0);
cbuffer Params : register(b0)
{
    float4 uvRect;      // xy: offset, zw: size of the picture within the texture, in uv
    float4 footprint;   // xy: one output pixel in uv; z: taps per axis
    float4 toR, toG, toB;
    float4 range;       // y offset, y scale, chroma offset, chroma scale
};
struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
VSOut vs(uint id : SV_VertexID)
{
    VSOut o;
    float2 t = float2((id << 1) & 2, id & 2);
    o.pos = float4(t * float2(2, -2) + float2(-1, 1), 0, 1);
    o.uv = t;
    return o;
}
float3 rgbAt(float2 uv)
{
    float y = (texY.SampleLevel(samp, uv, 0) - range.x) * range.y;
    float2 c = (texUV.SampleLevel(samp, uv, 0) - range.z) * range.w;
    float3 yuv = float3(y, c.x, c.y);
    return float3(dot(yuv, toR.xyz), dot(yuv, toG.xyz), dot(yuv, toB.xyz));
}
float4 ps(VSOut i) : SV_Target
{
    float2 uv = uvRect.xy + i.uv * uvRect.zw;
    int n = (int)footprint.z;
    if (n <= 1) return float4(saturate(rgbAt(uv)), 1);
    float3 acc = 0;
    [loop] for (int a = 0; a < n; a++)
    {
        [loop] for (int b = 0; b < n; b++)
        {
            float2 o = (float2(a, b) + 0.5) / n - 0.5;
            acc += rgbAt(uv + o * footprint.xy);
        }
    }
    return float4(saturate(acc / (n * n)), 1);
}";

    [StructLayout(LayoutKind.Sequential)]
    struct Params
    {
        public float RectX, RectY, RectW, RectH;
        public float FootX, FootY, Taps, Pad;
        public float R0, R1, R2, R3, G0, G1, G2, G3, B0, B1, B2, B3;
        public float YOffset, YScale, COffset, CScale;
    }

    readonly IntPtr device, context, hwnd;
    IntPtr factory, swapChain, rtv, waitable;
    IntPtr vs, ps, sampler, cbuffer;
    // The picture: an NV12 texture (GPU decoder) or one plane each (software decoder), with views.
    IntPtr nv12, planeY, planeUV, srvY, srvUV;
    int texWidth, texHeight;
    bool texIsNV12;
    uint backWidth, backHeight;
    bool fullRange = true, bt601;
    bool hasPicture;

    /// <summary>The part of the texture that is the picture (the SPS's size); 0 = all of it.</summary>
    public int PictureWidth { get; set; }
    public int PictureHeight { get; set; }

    /// <summary>Where the picture was last drawn, in the window's pixels.</summary>
    public System.Drawing.RectangleF PictureRect { get; private set; }

    /// <summary>Rows at the top of the picture left out (a Coherence window's Mac title bar, which its
    /// Windows frame replaces): the uv rect starts below them.</summary>
    public int CropTop { get; set; }
    /// <summary>Fill the whole target, stretched, rather than fit it with black bars: a Coherence
    /// window is the picture's own shape but for a moment during a resize, when the last frame is
    /// stretched until one of the new size comes.</summary>
    public bool Fill { get; set; }

    public Renderer(IntPtr device, IntPtr context, IntPtr hwnd, int width, int height)
    {
        this.device = device; this.context = context; this.hwnd = hwnd;
        Com.AddRef(device); Com.AddRef(context);
        factory = D3D.FactoryOf(device);
        backWidth = (uint)Math.Max(1, width); backHeight = (uint)Math.Max(1, height);
        var desc = new DXGI_SWAP_CHAIN_DESC1
        {
            Width = backWidth, Height = backHeight, Format = D3D.DXGI_FORMAT_B8G8R8A8_UNORM, SampleCount = 1,
            BufferUsage = D3D.DXGI_USAGE_RENDER_TARGET_OUTPUT, BufferCount = 2, Scaling = D3D.DXGI_SCALING_STRETCH,
            SwapEffect = D3D.DXGI_SWAP_EFFECT_FLIP_DISCARD, AlphaMode = D3D.DXGI_ALPHA_MODE_IGNORE,
            Flags = D3D.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT,
        };
        swapChain = D3D.CreateSwapChainForHwnd(factory, device, hwnd, desc);
        waitable = D3D.LowLatency(swapChain);
        MakeTarget();
        vs = D3D.CreateVertexShader(device, D3D.Compile(Shader, "vs", "vs_4_0"));
        ps = D3D.CreatePixelShader(device, D3D.Compile(Shader, "ps", "ps_4_0"));
        sampler = D3D.CreateLinearSampler(device);
        cbuffer = D3D.CreateDynamicConstantBuffer(device, (uint)((Marshal.SizeOf<Params>() + 15) / 16 * 16));
    }

    void MakeTarget()
    {
        var back = D3D.GetBuffer(swapChain);
        try { rtv = D3D.CreateRenderTargetView(device, back); }
        finally { Com.Release(back); }
    }

    public void Resize(int width, int height)
    {
        uint w = (uint)Math.Max(1, width), h = (uint)Math.Max(1, height);
        if (w == backWidth && h == backHeight) return;
        D3D.OMSetRenderTarget(context, IntPtr.Zero);
        Com.Release(ref rtv);
        D3D.Flush(context);
        int hr = D3D.ResizeBuffers(swapChain, 0, w, h, 0, D3D.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT);
        DeviceLostException.ThrowIfLost(hr);
        if (hr >= 0) { backWidth = w; backHeight = h; }
        // The picture's texture outlives the swap chain's buffers: a resize redraws it, no keyframe needed.
        MakeTarget();
    }

    /// <summary>Takes the decoder's picture (copied on the GPU, or uploaded from memory).</summary>
    public void Upload(in DecodedFrame f)
    {
        fullRange = f.FullRange; bt601 = f.Bt601;
        if (f.Texture != IntPtr.Zero)
        {
            if (nv12 == IntPtr.Zero || !texIsNV12 || texWidth != f.Width || texHeight != f.Height)
            {
                ReleasePicture();
                var desc = new D3D11_TEXTURE2D_DESC
                {
                    Width = (uint)f.Width, Height = (uint)f.Height, MipLevels = 1, ArraySize = 1, Format = D3D.DXGI_FORMAT_NV12,
                    SampleCount = 1, Usage = D3D.D3D11_USAGE_DEFAULT, BindFlags = D3D.D3D11_BIND_SHADER_RESOURCE,
                };
                nv12 = D3D.CreateTexture2D(device, desc);
                srvY = D3D.CreateShaderResourceView(device, nv12, D3D.DXGI_FORMAT_R8_UNORM);
                srvUV = D3D.CreateShaderResourceView(device, nv12, D3D.DXGI_FORMAT_R8G8_UNORM);
                texWidth = f.Width; texHeight = f.Height; texIsNV12 = true;
            }
            D3D.CopySubresourceRegion(context, nv12, 0, f.Texture, f.Subresource);
        }
        else if (f.Data != IntPtr.Zero)
        {
            if (planeY == IntPtr.Zero || texIsNV12 || texWidth != f.Width || texHeight != f.Height)
            {
                ReleasePicture();
                var y = new D3D11_TEXTURE2D_DESC
                {
                    Width = (uint)f.Width, Height = (uint)f.Height, MipLevels = 1, ArraySize = 1, Format = D3D.DXGI_FORMAT_R8_UNORM,
                    SampleCount = 1, Usage = D3D.D3D11_USAGE_DEFAULT, BindFlags = D3D.D3D11_BIND_SHADER_RESOURCE,
                };
                planeY = D3D.CreateTexture2D(device, y);
                var uv = y;
                uv.Width = (uint)(f.Width + 1) / 2; uv.Height = (uint)(f.Height + 1) / 2; uv.Format = D3D.DXGI_FORMAT_R8G8_UNORM;
                planeUV = D3D.CreateTexture2D(device, uv);
                srvY = D3D.CreateShaderResourceView(device, planeY, D3D.DXGI_FORMAT_R8_UNORM);
                srvUV = D3D.CreateShaderResourceView(device, planeUV, D3D.DXGI_FORMAT_R8G8_UNORM);
                texWidth = f.Width; texHeight = f.Height; texIsNV12 = false;
            }
            D3D.UpdateSubresource(context, planeY, f.Data, (uint)f.Pitch);
            D3D.UpdateSubresource(context, planeUV, f.Data + f.Pitch * f.Height, (uint)f.Pitch);
        }
        hasPicture = true;
    }

    /// <summary>Waits (at most a frame) until the swap chain takes another, so what is drawn is the newest.</summary>
    public void WaitForSlot()
    {
        if (waitable != IntPtr.Zero) WaitForSingleObjectEx(waitable, 50, true);
    }

    public void Draw()
    {
        if (Render(rtv, backWidth, backHeight) is { } rect) PictureRect = rect;
        DeviceLostException.ThrowIfLost(D3D.Present(swapChain, 0, 0));
    }

    (int w, int h) PictureSize() =>
        (PictureWidth > 0 ? Math.Min(PictureWidth, texWidth) : texWidth, PictureHeight > 0 ? Math.Min(PictureHeight, texHeight) : texHeight);

    /// <summary>The picture fitted into a target of this size: where it went, or null with none to draw.</summary>
    System.Drawing.RectangleF? Render(IntPtr target, uint width, uint height)
    {
        D3D.ClearState(context);
        D3D.OMSetRenderTarget(context, target);
        D3D.ClearRenderTargetView(context, target, 0, 0, 0);
        if (hasPicture && srvY != IntPtr.Zero)
        {
            var (pwi, phi) = PictureSize();
            int crop = Math.Clamp(CropTop, 0, Math.Max(0, phi - 1));
            float pw = pwi, ph = phi - crop;
            // Fit the picture in the window, centred, whole pixels (or fill it, for a Coherence window).
            float scale = Math.Min(width / pw, height / ph);
            float dw = MathF.Round(pw * scale), dh = MathF.Round(ph * scale);
            float dx = MathF.Floor((width - dw) / 2), dy = MathF.Floor((height - dh) / 2);
            if (Fill) { dw = width; dh = height; dx = 0; dy = 0; }
            D3D.RSSetViewport(context, new D3D11_VIEWPORT { TopLeftX = dx, TopLeftY = dy, Width = dw, Height = dh, MinDepth = 0, MaxDepth = 1 });

            var p = new Params
            {
                RectX = 0, RectY = crop / (float)texHeight, RectW = pw / texWidth, RectH = ph / texHeight,
                FootX = pw / dw / texWidth, FootY = ph / dh / texHeight,
                Taps = Math.Clamp(MathF.Ceiling(Math.Max(pw / dw, ph / dh) - 0.01f), 1, 4),
            };
            if (bt601) { (p.R0, p.R1, p.R2) = (1, 0, 1.402f); (p.G0, p.G1, p.G2) = (1, -0.344136f, -0.714136f); (p.B0, p.B1, p.B2) = (1, 1.772f, 0); }
            else { (p.R0, p.R1, p.R2) = (1, 0, 1.5748f); (p.G0, p.G1, p.G2) = (1, -0.1873f, -0.4681f); (p.B0, p.B1, p.B2) = (1, 1.8556f, 0); }
            if (fullRange) { p.YOffset = 0; p.YScale = 1; p.COffset = 128f / 255f; p.CScale = 1; }
            else { p.YOffset = 16f / 255f; p.YScale = 255f / 219f; p.COffset = 128f / 255f; p.CScale = 255f / 224f; }
            var m = D3D.Map(context, cbuffer, D3D.D3D11_MAP_WRITE_DISCARD);
            Marshal.StructureToPtr(p, m.pData, false);
            D3D.Unmap(context, cbuffer);

            D3D.IASetInputLayout(context, IntPtr.Zero);
            D3D.IASetPrimitiveTopology(context, D3D.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            D3D.VSSetShader(context, vs);
            D3D.PSSetShader(context, ps);
            D3D.PSSetShaderResources(context, srvY, srvUV);
            D3D.PSSetSampler(context, sampler);
            D3D.PSSetConstantBuffer(context, cbuffer);
            D3D.Draw(context, 3);
            return new System.Drawing.RectangleF(dx, dy, dw, dh);
        }
        return null;
    }

    /// <summary>The picture now, at its own size (a picture pixel to a bitmap pixel), through the same
    /// shader into a texture of its own and read back; null before the first picture.</summary>
    public System.Drawing.Bitmap? Capture()
    {
        if (!hasPicture || srvY == IntPtr.Zero) return null;
        var (pw, phAll) = PictureSize();
        int ph = phAll - Math.Clamp(CropTop, 0, Math.Max(0, phAll - 1));
        IntPtr target = IntPtr.Zero, view = IntPtr.Zero, staging = IntPtr.Zero;
        try
        {
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)pw, Height = (uint)ph, MipLevels = 1, ArraySize = 1, Format = D3D.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleCount = 1, Usage = D3D.D3D11_USAGE_DEFAULT, BindFlags = D3D.D3D11_BIND_RENDER_TARGET,
            };
            target = D3D.CreateTexture2D(device, desc);
            view = D3D.CreateRenderTargetView(device, target);
            Render(view, (uint)pw, (uint)ph);
            desc.Usage = D3D.D3D11_USAGE_STAGING; desc.BindFlags = 0; desc.CPUAccessFlags = D3D.D3D11_CPU_ACCESS_READ;
            staging = D3D.CreateTexture2D(device, desc);
            D3D.CopySubresourceRegion(context, staging, 0, target, 0);
            var m = D3D.Map(context, staging, D3D.D3D11_MAP_READ);
            try
            {
                var bmp = new System.Drawing.Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, pw, ph), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
                for (int y = 0; y < ph; y++)
                    Buffer.MemoryCopy((byte*)m.pData + (long)y * m.RowPitch, (byte*)data.Scan0 + (long)y * data.Stride, data.Stride, pw * 4L);
                bmp.UnlockBits(data);
                return bmp;
            }
            finally { D3D.Unmap(context, staging); }
        }
        finally
        {
            Com.Release(ref staging); Com.Release(ref view); Com.Release(ref target);
        }
    }

    public void ClearPicture()
    {
        hasPicture = false;
    }

    void ReleasePicture()
    {
        Com.Release(ref srvY); Com.Release(ref srvUV);
        Com.Release(ref nv12); Com.Release(ref planeY); Com.Release(ref planeUV);
        texWidth = texHeight = 0;
    }

    public void Dispose()
    {
        D3D.ClearState(context);
        ReleasePicture();
        Com.Release(ref cbuffer); Com.Release(ref sampler); Com.Release(ref ps); Com.Release(ref vs);
        Com.Release(ref rtv); Com.Release(ref swapChain); Com.Release(ref factory);
        if (waitable != IntPtr.Zero) { CloseHandle(waitable); waitable = IntPtr.Zero; }
        D3D.Flush(context);
        Com.Release(context); Com.Release(device);
    }

    [DllImport("kernel32.dll")] static extern uint WaitForSingleObjectEx(IntPtr handle, uint ms, bool alertable);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
