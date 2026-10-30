#if MANA_BROWSER
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;

namespace ManaGraphics.Code.Graphics.Browser
{
    // Maps Silk.NET's wgpu* names to the mana_wgpu* shims in mana_webgpu.c, since browser-wasm has no native
    // library to load. Keep the map in sync with Silk.NET.WebGPU.
    public sealed partial class ManaBrowserWebGpuNativeContext : INativeContext
    {
        private static readonly Dictionary<string, string> ShimNames = new()
        {
            ["wgpuAdapterEnumerateFeatures"] = "mana_wgpuAdapterEnumerateFeatures",
            ["wgpuAdapterGetLimits"] = "mana_wgpuAdapterGetLimits",
            ["wgpuAdapterGetProperties"] = "mana_wgpuAdapterGetProperties",
            ["wgpuAdapterRelease"] = "mana_wgpuAdapterRelease",
            ["wgpuAdapterRequestDevice"] = "mana_wgpuAdapterRequestDevice",
            ["wgpuBindGroupLayoutRelease"] = "mana_wgpuBindGroupLayoutRelease",
            ["wgpuBindGroupRelease"] = "mana_wgpuBindGroupRelease",
            ["wgpuBufferDestroy"] = "mana_wgpuBufferDestroy",
            ["wgpuBufferMapAsync"] = "mana_wgpuBufferMapAsync",
            ["wgpuBufferRelease"] = "mana_wgpuBufferRelease",
            ["wgpuCommandBufferRelease"] = "mana_wgpuCommandBufferRelease",
            ["wgpuCommandEncoderBeginComputePass"] = "mana_wgpuCommandEncoderBeginComputePass",
            ["wgpuCommandEncoderBeginRenderPass"] = "mana_wgpuCommandEncoderBeginRenderPass",
            ["wgpuCommandEncoderCopyBufferToBuffer"] = "mana_wgpuCommandEncoderCopyBufferToBuffer",
            ["wgpuCommandEncoderCopyTextureToTexture"] = "mana_wgpuCommandEncoderCopyTextureToTexture",
            ["wgpuCommandEncoderFinish"] = "mana_wgpuCommandEncoderFinish",
            ["wgpuCommandEncoderRelease"] = "mana_wgpuCommandEncoderRelease",
            ["wgpuComputePassEncoderDispatchWorkgroups"] = "mana_wgpuComputePassEncoderDispatchWorkgroups",
            ["wgpuComputePassEncoderEnd"] = "mana_wgpuComputePassEncoderEnd",
            ["wgpuComputePassEncoderRelease"] = "mana_wgpuComputePassEncoderRelease",
            ["wgpuComputePassEncoderSetBindGroup"] = "mana_wgpuComputePassEncoderSetBindGroup",
            ["wgpuComputePassEncoderSetPipeline"] = "mana_wgpuComputePassEncoderSetPipeline",
            ["wgpuComputePipelineRelease"] = "mana_wgpuComputePipelineRelease",
            ["wgpuCreateInstance"] = "mana_wgpuCreateInstance",
            ["wgpuInstanceCreateSurface"] = "mana_wgpuInstanceCreateSurface",
            ["wgpuDeviceCreateBindGroup"] = "mana_wgpuDeviceCreateBindGroup",
            ["wgpuDeviceCreateBindGroupLayout"] = "mana_wgpuDeviceCreateBindGroupLayout",
            ["wgpuDeviceCreateBuffer"] = "mana_wgpuDeviceCreateBuffer",
            ["wgpuDeviceCreateCommandEncoder"] = "mana_wgpuDeviceCreateCommandEncoder",
            ["wgpuDeviceCreateComputePipeline"] = "mana_wgpuDeviceCreateComputePipeline",
            ["wgpuDeviceCreatePipelineLayout"] = "mana_wgpuDeviceCreatePipelineLayout",
            ["wgpuDeviceCreateRenderPipeline"] = "mana_wgpuDeviceCreateRenderPipeline",
            ["wgpuDeviceCreateSampler"] = "mana_wgpuDeviceCreateSampler",
            ["wgpuDeviceCreateShaderModule"] = "mana_wgpuDeviceCreateShaderModule",
            ["wgpuDeviceCreateTexture"] = "mana_wgpuDeviceCreateTexture",
            ["wgpuDeviceEnumerateFeatures"] = "mana_wgpuDeviceEnumerateFeatures",
            ["wgpuDeviceGetLimits"] = "mana_wgpuDeviceGetLimits",
            ["wgpuDeviceGetQueue"] = "mana_wgpuDeviceGetQueue",
            ["wgpuDeviceRelease"] = "mana_wgpuDeviceRelease",
            ["wgpuDeviceSetUncapturedErrorCallback"] = "mana_wgpuDeviceSetUncapturedErrorCallback",
            ["wgpuInstanceProcessEvents"] = "mana_wgpuInstanceProcessEvents",
            ["wgpuInstanceRelease"] = "mana_wgpuInstanceRelease",
            ["wgpuInstanceRequestAdapter"] = "mana_wgpuInstanceRequestAdapter",
            ["wgpuPipelineLayoutRelease"] = "mana_wgpuPipelineLayoutRelease",
            ["wgpuQueueRelease"] = "mana_wgpuQueueRelease",
            ["wgpuQueueSubmit"] = "mana_wgpuQueueSubmit",
            ["wgpuQueueWriteBuffer"] = "mana_wgpuQueueWriteBuffer",
            ["wgpuQueueWriteTexture"] = "mana_wgpuQueueWriteTexture",
            ["wgpuRenderPassEncoderDraw"] = "mana_wgpuRenderPassEncoderDraw",
            ["wgpuRenderPassEncoderDrawIndexed"] = "mana_wgpuRenderPassEncoderDrawIndexed",
            ["wgpuRenderPassEncoderEnd"] = "mana_wgpuRenderPassEncoderEnd",
            ["wgpuRenderPassEncoderRelease"] = "mana_wgpuRenderPassEncoderRelease",
            ["wgpuRenderPassEncoderSetBindGroup"] = "mana_wgpuRenderPassEncoderSetBindGroup",
            ["wgpuRenderPassEncoderSetIndexBuffer"] = "mana_wgpuRenderPassEncoderSetIndexBuffer",
            ["wgpuRenderPassEncoderSetPipeline"] = "mana_wgpuRenderPassEncoderSetPipeline",
            ["wgpuRenderPassEncoderSetScissorRect"] = "mana_wgpuRenderPassEncoderSetScissorRect",
            ["wgpuRenderPassEncoderSetVertexBuffer"] = "mana_wgpuRenderPassEncoderSetVertexBuffer",
            ["wgpuRenderPipelineRelease"] = "mana_wgpuRenderPipelineRelease",
            ["wgpuRenderPipelineGetBindGroupLayout"] = "mana_wgpuRenderPipelineGetBindGroupLayout",
            ["wgpuSamplerRelease"] = "mana_wgpuSamplerRelease",
            ["wgpuShaderModuleRelease"] = "mana_wgpuShaderModuleRelease",
            ["wgpuSurfaceConfigure"] = "mana_wgpuSurfaceConfigure",
            ["wgpuSurfaceGetCurrentTexture"] = "mana_wgpuSurfaceGetCurrentTexture",
            ["wgpuSurfaceGetPreferredFormat"] = "mana_wgpuSurfaceGetPreferredFormat",
            ["wgpuSurfacePresent"] = "mana_wgpuSurfacePresent",
            ["wgpuSurfaceRelease"] = "mana_wgpuSurfaceRelease",
            ["wgpuSurfaceUnconfigure"] = "mana_wgpuSurfaceUnconfigure",
            ["wgpuTextureCreateView"] = "mana_wgpuTextureCreateView",
            ["wgpuTextureDestroy"] = "mana_wgpuTextureDestroy",
            ["wgpuTextureRelease"] = "mana_wgpuTextureRelease",
            ["wgpuTextureViewRelease"] = "mana_wgpuTextureViewRelease",
        };

        private static nint _libraryHandle;

        static ManaBrowserWebGpuNativeContext()
        {
            // Keeps the registrations below reachable so the P/Invoke table generator does not trim them; never true at
            // runtime, but not a constant the compiler can fold.
            if (Environment.TickCount64 < 0)
            {
                _ = mana_wgpuAdapterEnumerateFeatures_reg();
                _ = mana_wgpuAdapterGetLimits_reg();
                _ = mana_wgpuAdapterGetProperties_reg();
                _ = mana_wgpuAdapterRelease_reg();
                _ = mana_wgpuAdapterRequestDevice_reg();
                _ = mana_wgpuBindGroupLayoutRelease_reg();
                _ = mana_wgpuBindGroupRelease_reg();
                _ = mana_wgpuBufferDestroy_reg();
                _ = mana_wgpuBufferMapAsync_reg();
                _ = mana_wgpuBufferRelease_reg();
                _ = mana_wgpuCommandBufferRelease_reg();
                _ = mana_wgpuCommandEncoderBeginComputePass_reg();
                _ = mana_wgpuCommandEncoderBeginRenderPass_reg();
                _ = mana_wgpuCommandEncoderCopyBufferToBuffer_reg();
                _ = mana_wgpuCommandEncoderCopyTextureToTexture_reg();
                _ = mana_wgpuCommandEncoderFinish_reg();
                _ = mana_wgpuCommandEncoderRelease_reg();
                _ = mana_wgpuComputePassEncoderDispatchWorkgroups_reg();
                _ = mana_wgpuComputePassEncoderEnd_reg();
                _ = mana_wgpuComputePassEncoderRelease_reg();
                _ = mana_wgpuComputePassEncoderSetBindGroup_reg();
                _ = mana_wgpuComputePassEncoderSetPipeline_reg();
                _ = mana_wgpuComputePipelineRelease_reg();
                _ = mana_wgpuCreateInstance_reg();
                _ = mana_wgpuInstanceCreateSurface_reg();
                _ = mana_wgpuDeviceCreateBindGroup_reg();
                _ = mana_wgpuDeviceCreateBindGroupLayout_reg();
                _ = mana_wgpuDeviceCreateBuffer_reg();
                _ = mana_wgpuDeviceCreateCommandEncoder_reg();
                _ = mana_wgpuDeviceCreateComputePipeline_reg();
                _ = mana_wgpuDeviceCreatePipelineLayout_reg();
                _ = mana_wgpuDeviceCreateRenderPipeline_reg();
                _ = mana_wgpuDeviceCreateSampler_reg();
                _ = mana_wgpuDeviceCreateShaderModule_reg();
                _ = mana_wgpuDeviceCreateTexture_reg();
                _ = mana_wgpuDeviceEnumerateFeatures_reg();
                _ = mana_wgpuDeviceGetLimits_reg();
                _ = mana_wgpuDeviceGetQueue_reg();
                _ = mana_wgpuDeviceRelease_reg();
                _ = mana_wgpuDeviceSetUncapturedErrorCallback_reg();
                _ = mana_wgpuInstanceProcessEvents_reg();
                _ = mana_wgpuInstanceRelease_reg();
                _ = mana_wgpuInstanceRequestAdapter_reg();
                _ = mana_wgpuPipelineLayoutRelease_reg();
                _ = mana_wgpuQueueRelease_reg();
                _ = mana_wgpuQueueSubmit_reg();
                _ = mana_wgpuQueueWriteBuffer_reg();
                _ = mana_wgpuQueueWriteTexture_reg();
                _ = mana_wgpuRenderPassEncoderDraw_reg();
                _ = mana_wgpuRenderPassEncoderDrawIndexed_reg();
                _ = mana_wgpuRenderPassEncoderEnd_reg();
                _ = mana_wgpuRenderPassEncoderRelease_reg();
                _ = mana_wgpuRenderPassEncoderSetBindGroup_reg();
                _ = mana_wgpuRenderPassEncoderSetIndexBuffer_reg();
                _ = mana_wgpuRenderPassEncoderSetPipeline_reg();
                _ = mana_wgpuRenderPassEncoderSetScissorRect_reg();
                _ = mana_wgpuRenderPassEncoderSetVertexBuffer_reg();
                _ = mana_wgpuRenderPipelineRelease_reg();
                _ = mana_wgpuRenderPipelineGetBindGroupLayout_reg();
                _ = mana_wgpuSamplerRelease_reg();
                _ = mana_wgpuShaderModuleRelease_reg();
                _ = mana_wgpuSurfaceConfigure_reg();
                _ = mana_wgpuSurfaceGetCurrentTexture_reg();
                _ = mana_wgpuSurfaceGetPreferredFormat_reg();
                _ = mana_wgpuSurfacePresent_reg();
                _ = mana_wgpuSurfaceRelease_reg();
                _ = mana_wgpuSurfaceUnconfigure_reg();
                _ = mana_wgpuTextureCreateView_reg();
                _ = mana_wgpuTextureDestroy_reg();
                _ = mana_wgpuTextureRelease_reg();
                _ = mana_wgpuTextureViewRelease_reg();
            }
        }

        public nint GetProcAddress(string proc, int? slot = null)
        {
            Console.WriteLine($"[NativeContext] GetProcAddress('{proc}', slot={slot})");
            if (TryGetProcAddress(proc, out var addr, slot))
            {
                Console.WriteLine($"[NativeContext] -> resolved to 0x{addr:X}");
                return addr;
            }

            Console.WriteLine($"[NativeContext] -> NOT FOUND, throwing.");
            throw new PlatformNotSupportedException(
                $"Browser WebGPU shim for '{proc}' not found in mana_webgpu.c.");
        }

        public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
        {
            addr = 0;
            if (!ShimNames.TryGetValue(proc, out var shimName))
            {
                Console.WriteLine($"[NativeContext] TryGetProcAddress('{proc}'): no ShimNames entry.");
                return false;
            }

            if (_libraryHandle == 0 && !NativeLibrary.TryLoad("mana_webgpu", out _libraryHandle))
            {
                Console.WriteLine($"[NativeContext] TryGetProcAddress('{proc}'): NativeLibrary.TryLoad(\"mana_webgpu\") failed.");
                return false;
            }

            var found = NativeLibrary.TryGetExport(_libraryHandle, shimName, out addr);
            Console.WriteLine($"[NativeContext] TryGetProcAddress('{proc}') -> shim='{shimName}', found={found}, addr=0x{addr:X}");
            return found;
        }

        public void Dispose()
        {
        }

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuAdapterEnumerateFeatures")]
        private static partial nint mana_wgpuAdapterEnumerateFeatures_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuAdapterGetLimits")]
        private static partial nint mana_wgpuAdapterGetLimits_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuAdapterGetProperties")]
        private static partial nint mana_wgpuAdapterGetProperties_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuAdapterRelease")]
        private static partial nint mana_wgpuAdapterRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuAdapterRequestDevice")]
        private static partial nint mana_wgpuAdapterRequestDevice_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuBindGroupLayoutRelease")]
        private static partial nint mana_wgpuBindGroupLayoutRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuBindGroupRelease")]
        private static partial nint mana_wgpuBindGroupRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuBufferDestroy")]
        private static partial nint mana_wgpuBufferDestroy_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuBufferMapAsync")]
        private static partial nint mana_wgpuBufferMapAsync_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuBufferRelease")]
        private static partial nint mana_wgpuBufferRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuCommandBufferRelease")]
        private static partial nint mana_wgpuCommandBufferRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuCommandEncoderBeginComputePass")]
        private static partial nint mana_wgpuCommandEncoderBeginComputePass_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuCommandEncoderBeginRenderPass")]
        private static partial nint mana_wgpuCommandEncoderBeginRenderPass_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuCommandEncoderCopyBufferToBuffer")]
        private static partial nint mana_wgpuCommandEncoderCopyBufferToBuffer_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuCommandEncoderCopyTextureToTexture")]
        private static partial nint mana_wgpuCommandEncoderCopyTextureToTexture_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuCommandEncoderFinish")]
        private static partial nint mana_wgpuCommandEncoderFinish_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuCommandEncoderRelease")]
        private static partial nint mana_wgpuCommandEncoderRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuComputePassEncoderDispatchWorkgroups")]
        private static partial nint mana_wgpuComputePassEncoderDispatchWorkgroups_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuComputePassEncoderEnd")]
        private static partial nint mana_wgpuComputePassEncoderEnd_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuComputePassEncoderRelease")]
        private static partial nint mana_wgpuComputePassEncoderRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuComputePassEncoderSetBindGroup")]
        private static partial nint mana_wgpuComputePassEncoderSetBindGroup_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuComputePassEncoderSetPipeline")]
        private static partial nint mana_wgpuComputePassEncoderSetPipeline_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuComputePipelineRelease")]
        private static partial nint mana_wgpuComputePipelineRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuCreateInstance")]
        private static partial nint mana_wgpuCreateInstance_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuInstanceCreateSurface")]
        private static partial nint mana_wgpuInstanceCreateSurface_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateBindGroup")]
        private static partial nint mana_wgpuDeviceCreateBindGroup_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateBindGroupLayout")]
        private static partial nint mana_wgpuDeviceCreateBindGroupLayout_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateBuffer")]
        private static partial nint mana_wgpuDeviceCreateBuffer_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateCommandEncoder")]
        private static partial nint mana_wgpuDeviceCreateCommandEncoder_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateComputePipeline")]
        private static partial nint mana_wgpuDeviceCreateComputePipeline_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreatePipelineLayout")]
        private static partial nint mana_wgpuDeviceCreatePipelineLayout_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateRenderPipeline")]
        private static partial nint mana_wgpuDeviceCreateRenderPipeline_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateSampler")]
        private static partial nint mana_wgpuDeviceCreateSampler_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateShaderModule")]
        private static partial nint mana_wgpuDeviceCreateShaderModule_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceCreateTexture")]
        private static partial nint mana_wgpuDeviceCreateTexture_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceEnumerateFeatures")]
        private static partial nint mana_wgpuDeviceEnumerateFeatures_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceGetLimits")]
        private static partial nint mana_wgpuDeviceGetLimits_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceGetQueue")]
        private static partial nint mana_wgpuDeviceGetQueue_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceRelease")]
        private static partial nint mana_wgpuDeviceRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuDeviceSetUncapturedErrorCallback")]
        private static partial nint mana_wgpuDeviceSetUncapturedErrorCallback_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuInstanceProcessEvents")]
        private static partial nint mana_wgpuInstanceProcessEvents_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuInstanceRelease")]
        private static partial nint mana_wgpuInstanceRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuInstanceRequestAdapter")]
        private static partial nint mana_wgpuInstanceRequestAdapter_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuPipelineLayoutRelease")]
        private static partial nint mana_wgpuPipelineLayoutRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuQueueRelease")]
        private static partial nint mana_wgpuQueueRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuQueueSubmit")]
        private static partial nint mana_wgpuQueueSubmit_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuQueueWriteBuffer")]
        private static partial nint mana_wgpuQueueWriteBuffer_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuQueueWriteTexture")]
        private static partial nint mana_wgpuQueueWriteTexture_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderDraw")]
        private static partial nint mana_wgpuRenderPassEncoderDraw_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderDrawIndexed")]
        private static partial nint mana_wgpuRenderPassEncoderDrawIndexed_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderEnd")]
        private static partial nint mana_wgpuRenderPassEncoderEnd_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderRelease")]
        private static partial nint mana_wgpuRenderPassEncoderRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderSetBindGroup")]
        private static partial nint mana_wgpuRenderPassEncoderSetBindGroup_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderSetIndexBuffer")]
        private static partial nint mana_wgpuRenderPassEncoderSetIndexBuffer_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderSetPipeline")]
        private static partial nint mana_wgpuRenderPassEncoderSetPipeline_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderSetScissorRect")]
        private static partial nint mana_wgpuRenderPassEncoderSetScissorRect_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPassEncoderSetVertexBuffer")]
        private static partial nint mana_wgpuRenderPassEncoderSetVertexBuffer_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPipelineRelease")]
        private static partial nint mana_wgpuRenderPipelineRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuRenderPipelineGetBindGroupLayout")]
        private static partial nint mana_wgpuRenderPipelineGetBindGroupLayout_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuSamplerRelease")]
        private static partial nint mana_wgpuSamplerRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuShaderModuleRelease")]
        private static partial nint mana_wgpuShaderModuleRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuSurfaceConfigure")]
        private static partial nint mana_wgpuSurfaceConfigure_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuSurfaceGetCurrentTexture")]
        private static partial nint mana_wgpuSurfaceGetCurrentTexture_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuSurfaceGetPreferredFormat")]
        private static partial nint mana_wgpuSurfaceGetPreferredFormat_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuSurfacePresent")]
        private static partial nint mana_wgpuSurfacePresent_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuSurfaceRelease")]
        private static partial nint mana_wgpuSurfaceRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuSurfaceUnconfigure")]
        private static partial nint mana_wgpuSurfaceUnconfigure_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuTextureCreateView")]
        private static partial nint mana_wgpuTextureCreateView_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuTextureDestroy")]
        private static partial nint mana_wgpuTextureDestroy_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuTextureRelease")]
        private static partial nint mana_wgpuTextureRelease_reg();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_wgpuTextureViewRelease")]
        private static partial nint mana_wgpuTextureViewRelease_reg();
    }
}
#endif
