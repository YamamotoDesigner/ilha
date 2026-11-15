using System;
using System.Runtime.InteropServices;

namespace Ilha
{
    /// <summary>
    /// Lê o volume do alto-falante padrão (Core Audio). Consultado pelo relógio do app, então
    /// não precisa de callbacks em outra thread; troca de dispositivo é detectada pelo ID.
    /// </summary>
    internal sealed class VolumeMonitor
    {
        private IAudioEndpointVolume? _endpoint;
        private string? _deviceId;
        private DateTime _nextRefresh;

        /// <summary>Volume de 0 a 1 e mudo, ou null se não houver saída de áudio.</summary>
        public (float Level, bool Muted)? Read(out bool deviceChanged)
        {
            deviceChanged = false;
            try
            {
                if (_endpoint == null || DateTime.Now >= _nextRefresh)
                {
                    _nextRefresh = DateTime.Now.AddSeconds(4);
                    var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                    try
                    {
                        if (enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device) != 0 || device == null)
                        {
                            Release();
                            return null;
                        }
                        device.GetId(out string id);
                        if (id != _deviceId || _endpoint == null)
                        {
                            Release();
                            var iid = typeof(IAudioEndpointVolume).GUID;
                            if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out object obj) != 0) return null;
                            _endpoint = (IAudioEndpointVolume)obj;
                            _deviceId = id;
                            deviceChanged = true;
                        }
                        Marshal.ReleaseComObject(device);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(enumerator);
                    }
                }

                _endpoint!.GetMasterVolumeLevelScalar(out float level);
                _endpoint.GetMute(out bool muted);
                return (level, muted);
            }
            catch
            {
                Release();
                return null;
            }
        }

        private void Release()
        {
            if (_endpoint != null) Marshal.ReleaseComObject(_endpoint);
            _endpoint = null;
            _deviceId = null;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
    }
}
