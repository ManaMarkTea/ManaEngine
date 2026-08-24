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
            StartAdapterRequest();
            Console.WriteLine("[BrowserAsync] StartAdapterRequest returned synchronously; awaiting callback...");
            await _adapterRequestTcs.Task;
            Console.WriteLine("[BrowserAsync] Adapter callback arrived.");
        }

        private async Task RequestDeviceAsync()
        {
            _deviceRequestTcs = new TaskCompletionSource<bool>();
            StartDeviceRequest();
            Console.WriteLine("[BrowserAsync] StartDeviceRequest returned synchronously; awaiting callback...");
            await _deviceRequestTcs.Task;
            Console.WriteLine("[BrowserAsync] Device callback arrived.");
        }
    }
}
#endif
