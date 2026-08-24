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
        /// (instance + surface creation). Not yet confirmed working against a real browser.
        /// </summary>
        public async Task CompleteBrowserInitializationAsync()
        {
            Console.WriteLine("[BrowserAsync] Requesting WebGPU adapter...");
            await RequestAdapterAsync();
            Console.WriteLine("[BrowserAsync] Adapter granted; requesting WebGPU device...");
            await RequestDeviceAsync();
            Console.WriteLine("[BrowserAsync] Device granted.");
            FinishInitialization(_requestedBackend, _window);
        }

        private async Task RequestAdapterAsync()
        {
            _adapterRequestTcs = new TaskCompletionSource<bool>();
            ManaBrowserWebGpuCallbackBridge.PendingContext = this;
            ManaBrowserWebGpuCallbackBridge.RequestAdapter();
            Console.WriteLine("[BrowserAsync] mana_request_adapter returned synchronously; awaiting JS Promise...");
            await _adapterRequestTcs.Task;
            Console.WriteLine("[BrowserAsync] Adapter result arrived.");
        }

        private async Task RequestDeviceAsync()
        {
            _deviceRequestTcs = new TaskCompletionSource<bool>();
            ManaBrowserWebGpuCallbackBridge.PendingContext = this;
            ManaBrowserWebGpuCallbackBridge.RequestDevice(AdapterIdForBrowser);
            Console.WriteLine("[BrowserAsync] mana_request_device returned synchronously; awaiting JS Promise...");
            await _deviceRequestTcs.Task;
            Console.WriteLine("[BrowserAsync] Device result arrived.");
        }

        /// <summary>
        /// Called from ManaBrowserWebGpuCallbackBridge's [JSExport] (JS calling back into C#, not
        /// the broken native-callback path -- see mana_webgpu.c). Not itself unsafe; the actual
        /// pointer assignment is in ManaWebGpuContext.Device.cs's SetAdapterFromId, since this
        /// partial declaration deliberately isn't `unsafe` (see the class-level remarks).
        /// </summary>
        internal void OnAdapterResultFromJs(int adapterId)
        {
            if (adapterId == 0)
            {
                _adapterRequestTcs?.TrySetException(
                    new InvalidOperationException("WebGPU adapter request failed (see browser console for the JS-side error)."));
                return;
            }

            SetAdapterFromId(adapterId);
            _adapterRequestTcs?.TrySetResult(true);
        }

        internal void OnDeviceResultFromJs(int deviceId)
        {
            if (deviceId == 0)
            {
                _deviceRequestTcs?.TrySetException(
                    new InvalidOperationException("WebGPU device request failed (see browser console for the JS-side error)."));
                return;
            }

            SetDeviceFromId(deviceId);
            _deviceRequestTcs?.TrySetResult(true);
        }
    }
}
#endif
