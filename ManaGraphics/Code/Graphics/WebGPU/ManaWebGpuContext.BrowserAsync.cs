#if MANA_BROWSER
using System;
using System.Threading.Tasks;
using ManaGraphics.Code.Diagnostics;

namespace ManaGraphics.Code.Graphics
{
    // Not unsafe, unlike the other partials: C# forbids await anywhere in an unsafe type, so the browser's
    // async bring-up lives here and calls small unsafe helpers in Device.cs.
    public sealed partial class ManaWebGpuContext
    {
        /// <summary>Awaited once after constructing a browser context, before anything else uses it.</summary>
        public async Task CompleteBrowserInitializationAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                Console.WriteLine("[BrowserAsync] Requesting WebGPU adapter...");
                await RequestAdapterAsync();
                ObjectDisposedException.ThrowIf(_disposed, this);
                Console.WriteLine("[BrowserAsync] Adapter granted; requesting WebGPU device...");
                await RequestDeviceAsync();
                ObjectDisposedException.ThrowIf(_disposed, this);
                Console.WriteLine("[BrowserAsync] Device granted.");
                FinishInitialization(_requestedBackend, _window);
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                CancelBrowserInitialization();
            }
        }

        private async Task RequestAdapterAsync()
        {
            _adapterRequestTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _adapterRequestId = ManaWebGpuInitializationRequests.Register(OnAdapterResultFromJs);
            ManaBrowserWebGpuCallbackBridge.RequestAdapter(_adapterRequestId);
            Console.WriteLine("[BrowserAsync] mana_request_adapter returned synchronously; awaiting JS Promise...");
            await _adapterRequestTcs.Task;
            Console.WriteLine("[BrowserAsync] Adapter result arrived.");
        }

        private async Task RequestDeviceAsync()
        {
            _deviceRequestTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _deviceRequestId = ManaWebGpuInitializationRequests.Register(OnDeviceResultFromJs);
            ManaBrowserWebGpuCallbackBridge.RequestDevice(AdapterIdForBrowser, _deviceRequestId);
            Console.WriteLine("[BrowserAsync] mana_request_device returned synchronously; awaiting JS Promise...");
            await _deviceRequestTcs.Task;
            Console.WriteLine("[BrowserAsync] Device result arrived.");
        }

        private int _adapterRequestId;
        private int _deviceRequestId;

        private void CancelBrowserInitialization()
        {
            ManaWebGpuInitializationRequests.Cancel(_adapterRequestId);
            ManaWebGpuInitializationRequests.Cancel(_deviceRequestId);
            _adapterRequestId = 0;
            _deviceRequestId = 0;
            _adapterRequestTcs?.TrySetCanceled();
            _deviceRequestTcs?.TrySetCanceled();
        }

        internal bool OnAdapterResultFromJs(int adapterId)
        {
            if (_disposed) { return false; }
            if (adapterId == 0)
            {
                _adapterRequestTcs?.TrySetException(
                    new InvalidOperationException("WebGPU adapter request failed (see browser console for the JS-side error)."));
                return true;
            }

            SetAdapterFromId(adapterId);
            _adapterRequestTcs?.TrySetResult(true);
            return true;
        }

        internal bool OnDeviceResultFromJs(int deviceId)
        {
            if (_disposed) { return false; }
            if (deviceId == 0)
            {
                _deviceRequestTcs?.TrySetException(
                    new InvalidOperationException("WebGPU device request failed (see browser console for the JS-side error)."));
                return true;
            }

            SetDeviceFromId(deviceId);
            _deviceRequestTcs?.TrySetResult(true);
            return true;
        }
    }
}
#endif
