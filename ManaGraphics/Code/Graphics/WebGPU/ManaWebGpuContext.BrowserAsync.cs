#if MANA_BROWSER
using System;
using System.Threading.Tasks;
using ManaGraphics.Code.Diagnostics;

namespace ManaGraphics.Code.Graphics
{
    // Deliberately NOT declared `unsafe`, unlike every other ManaWebGpuContext.*.cs partial file:
    // C# doesn't allow `await` inside an unsafe context at all (the whole textual extent of an
    // `unsafe class` counts, not just methods that touch a pointer themselves), so the async
    // adapter/device bring-up this class needs for a real browser has to live in its own
    // non-unsafe partial declaration, calling into small unsafe non-async helpers
    // (StartAdapterRequest/StartDeviceRequest, in ManaWebGpuContext.Device.cs) for the pointer-
    // touching parts. See ManaWebGpuContext.cs's constructor comment for the full reasoning.
    public sealed partial class ManaWebGpuContext
    {
        /// <summary>
        /// Must be awaited once, right after constructing a browser ManaWebGpuContext, before it's
        /// usable for anything else -- the constructor itself only does the synchronous part
        /// (instance + surface creation).
        /// </summary>
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
