﻿namespace TinyAudio.Wasapi;

using System;
using System.Runtime.Versioning;

using TinyAudio.Wasapi.Interop;

[SupportedOSPlatform("windows")]
internal sealed class MediaDevice
{
    private const uint CLSCTX_ALL = 1 | 2 | 4 | 16;
    private readonly unsafe MMDeviceInst* _device;
    private static readonly Lazy<MediaDevice> _defaultDevice = new Lazy<MediaDevice>(GetDefaultDevice);

    private unsafe MediaDevice(MMDeviceInst* device) => this._device = device;

    public static MediaDevice Default => _defaultDevice.Value;

    public AudioClient CreateAudioClient()
    {
        unsafe
        {
            Guid iid = Guids.IID_IAudioClient;
            AudioClientInst* audioClientInst = null;
            uint res = this._device->Vtbl->Activate(this._device, &iid, CLSCTX_ALL, null, (void**)&audioClientInst);
            if (res != 0)
                throw new InvalidOperationException();

            return new AudioClient(audioClientInst);
        }
    }

    private static MediaDevice GetDefaultDevice()
    {
        unsafe
        {
            Guid clsid = Guids.CLSID_MMDeviceEnumerator;
            Guid iid = Guids.IID_IMMDeviceEnumerator;
            DeviceEnumeratorInst* enumInst = null;
            try
            {
                uint res = NativeMethods.CoCreateInstance(&clsid, null, CLSCTX_ALL, &iid, (void**)&enumInst);
                if (res != 0)
                    throw new InvalidOperationException();

                MMDeviceInst* deviceInst = null;
                res = enumInst->Vtbl->GetDefaultAudioEndpoint(enumInst, EDataFlow.eRender, ERole.eConsole, &deviceInst);
                if (res != 0)
                    throw new InvalidOperationException();

                return new MediaDevice(deviceInst);
            }
            finally
            {
                if (enumInst != null)
                    enumInst->Vtbl->Release(enumInst);
            }
        }
    }
}
