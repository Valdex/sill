using System.Runtime.InteropServices;

namespace Sill.Windows.Interop;

// The waveOut API (winmm.dll, mmeapi.h): the oldest of Windows' sound APIs and the simplest, PCM in
// buffers handed to the sound card in order, which Windows mixes through its audio engine like any
// other. Enough for the Mac's sound as plain PCM; no COM, no package. The structures as the Windows
// SDK's headers declare them, byte-packed (mmsyscom.h: "Assume byte packing throughout").
public static unsafe class WinMM
{
    public const uint WAVE_MAPPER = 0xFFFFFFFF;
    public const uint CALLBACK_EVENT = 0x00050000;
    public const uint WHDR_DONE = 0x01, WHDR_PREPARED = 0x02, WHDR_INQUEUE = 0x10;
    public const ushort WAVE_FORMAT_PCM = 1;
    public const uint MMSYSERR_NOERROR = 0;

    /// <summary>18 bytes.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WAVEFORMATEX
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    /// <summary>48 bytes on a 64-bit Windows (every field falls on its own size's boundary, packed or not).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength, dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags, dwLoops;
        public IntPtr lpNext, reserved;
    }

    [DllImport("winmm.dll")] public static extern uint waveOutOpen(out IntPtr hwo, uint deviceID, in WAVEFORMATEX format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] public static extern uint waveOutPrepareHeader(IntPtr hwo, WAVEHDR* hdr, uint size);
    [DllImport("winmm.dll")] public static extern uint waveOutUnprepareHeader(IntPtr hwo, WAVEHDR* hdr, uint size);
    [DllImport("winmm.dll")] public static extern uint waveOutWrite(IntPtr hwo, WAVEHDR* hdr, uint size);
    [DllImport("winmm.dll")] public static extern uint waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] public static extern uint waveOutClose(IntPtr hwo);
}
