#include <webgpu/webgpu.h>
#include <stddef.h>
#include <stdint.h>
#include <emscripten/em_js.h>

// Bypasses library_webgpu.js's own wgpuInstanceRequestAdapter/wgpuAdapterRequestDevice
// entirely for these two calls: their callback-delivery path (a `dynCall`/getWasmTableEntry
// invocation of the C# callback function pointer) hits a confirmed Emscripten runtime bug
// (RuntimeError: getWasmTableEntry(...) is not a function) when combined with .NET's wasm
// bundling -- reproducible, not fixed by AOT compilation, not fixed by explicitly forcing
// getWasmTableEntry past tree-shaking. See WebPort-Plan.md M2.
//
// The actual browser-vendor async operations (navigator.gpu.requestAdapter()/
// adapter.requestDevice(), both real Promises) still have to happen somehow, so these use
// EM_JS instead: plain synchronous JS injected directly into this compilation unit (not
// routed through library_webgpu.js's callback machinery at all), which starts the Promise
// and, on resolution, registers the resulting JS object into library_webgpu.js's OWN
// internal id-to-object tables (WebGPU.mgrAdapter/WebGPU.mgrDevice/WebGPU.mgrQueue) --
// exactly what library_webgpu.js's own working implementation does internally, replicated
// here by reading its source (src/library_webgpu.js in this Emscripten install) rather than
// guessed. This keeps every OTHER already-proven-working wgpu* call (AdapterGetLimits,
// DeviceCreateBuffer, DeviceGetQueue, etc.) operating on ordinary integer handles, unmodified.
// The result is delivered back to C# via a [JSExport] call (globalThis.ManaExports, the same
// mechanism ManaBrowserInterop.cs already uses successfully for the RAF loop/resize), not a
// native callback -- so it never touches the broken code path at all.
// wasm-ld can only --export a symbol that has a real defined body in the linked object; an
// EM_JS function's C-visible name is just an `extern` declaration satisfied later by JS (an
// import, not an export), so trying to put mana_request_adapter/mana_request_device directly
// into EmccExportedFunction fails at link time ("symbol exported via --export not found").
// Confirmed live: naming the EM_JS function itself and exporting a tiny real wrapper around it
// (mirroring the mana_wgpuXxx forwarding pattern below) fixes it.
EM_JS(void, mana_request_adapter_impl, (), {
    if (!('gpu' in navigator)) {
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.ManaBrowserWebGpuCallbackBridge.OnAdapterResult(0);
        }
        return;
    }
    navigator.gpu.requestAdapter().then((adapter) => {
        var adapterId = adapter ? WebGPU.mgrAdapter.create(adapter) : 0;
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.ManaBrowserWebGpuCallbackBridge.OnAdapterResult(adapterId);
        }
    }, (ex) => {
        console.error('mana_request_adapter failed', ex);
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.ManaBrowserWebGpuCallbackBridge.OnAdapterResult(0);
        }
    });
});

EM_JS(void, mana_request_device_impl, (int adapterId), {
    var adapter = WebGPU.mgrAdapter.get(adapterId);
    adapter.requestDevice().then((device) => {
        // Mirrors library_webgpu.js's own wgpuAdapterRequestDevice exactly: DeviceGetQueue
        // later reads WebGPU.mgrDevice.objects[deviceId].queueId, so the default queue has to
        // be pre-registered into the device's wrapper object at creation time, not lazily.
        var deviceWrapper = { queueId: WebGPU.mgrQueue.create(device.queue) };
        var deviceId = WebGPU.mgrDevice.create(device, deviceWrapper);
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.ManaBrowserWebGpuCallbackBridge.OnDeviceResult(deviceId);
        }
    }, (ex) => {
        console.error('mana_request_device failed', ex);
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.ManaBrowserWebGpuCallbackBridge.OnDeviceResult(0);
        }
    });
});

void mana_request_adapter(void) {
    mana_request_adapter_impl();
}

void mana_request_device(int adapterId) {
    mana_request_device_impl(adapterId);
}

// ---- Enum ABI remapping between Silk.NET.WebGPU 2.23.0 and this Emscripten SDK's bundled
// webgpu.h ----
// Both target structurally identical structs (every struct relevant to the render pipeline
// path was checked field-for-field against Silk.NET.WebGPU.dll decompiled via ilspycmd: same
// order, same shape, no insertions/removals), but many enums were renumbered between the two
// webgpu.h revisions -- discovered by mechanically diffing every Silk.NET.WebGPU enum's
// declared ordinals against this exact installed header (see WebPort-Plan.md's "M2 CLOSED"
// section, findings #3, for the two found and fixed earlier this same way: TextureDimension,
// TextureAspect, inline in their own shim functions instead of here since only one field each).
// Most of the rest are a uniform "add 1" (native inserted an Undefined=0 member Silk.NET's
// enum doesn't have, shifting everything after it by exactly one -- confirmed uniform across
// every member, not just the ones that happened to collide). Three
// (AddressMode/CompareFunction/VertexStepMode) are genuinely reordered, not just shifted, and
// need an explicit table. NOT yet live-verified: no live browser session was available to
// confirm the render pipeline actually creates successfully with these in place -- see
// WebPort-Plan.md's M3 prep note for why the demo pipeline itself is still deliberately
// disabled.
static WGPUPrimitiveTopology mana_remap_primitive_topology(WGPUPrimitiveTopology v) { return (WGPUPrimitiveTopology)((int)v + 1); }
static WGPUFrontFace mana_remap_front_face(WGPUFrontFace v) { return (WGPUFrontFace)((int)v + 1); }
static WGPUCullMode mana_remap_cull_mode(WGPUCullMode v) { return (WGPUCullMode)((int)v + 1); }
static WGPUStencilOperation mana_remap_stencil_operation(WGPUStencilOperation v) { return (WGPUStencilOperation)((int)v + 1); }
static WGPUFilterMode mana_remap_filter_mode(WGPUFilterMode v) { return (WGPUFilterMode)((int)v + 1); }
static WGPUMipmapFilterMode mana_remap_mipmap_filter_mode(WGPUMipmapFilterMode v) { return (WGPUMipmapFilterMode)((int)v + 1); }
static WGPUBlendFactor mana_remap_blend_factor(WGPUBlendFactor v) { return (WGPUBlendFactor)((int)v + 1); }
static WGPUBlendOperation mana_remap_blend_operation(WGPUBlendOperation v) { return (WGPUBlendOperation)((int)v + 1); }

static WGPUAddressMode mana_remap_address_mode(WGPUAddressMode v) {
    // Silk.NET: Repeat=0, MirrorRepeat=1, ClampToEdge=2 (no Undefined).
    // Native:   Undefined=0, ClampToEdge=1, Repeat=2, MirrorRepeat=3.
    switch ((int)v) {
        case 0: return WGPUAddressMode_Repeat;
        case 1: return WGPUAddressMode_MirrorRepeat;
        case 2: return WGPUAddressMode_ClampToEdge;
        default: return WGPUAddressMode_Undefined;
    }
}

static WGPUCompareFunction mana_remap_compare_function(WGPUCompareFunction v) {
    // Silk.NET: Undefined=0, Never=1, Less=2, LessEqual=3, Greater=4, GreaterEqual=5, Equal=6,
    //           NotEqual=7, Always=8.
    // Native:   Undefined=0, Never=1, Less=2, Equal=3, LessEqual=4, Greater=5, NotEqual=6,
    //           GreaterEqual=7, Always=8.
    switch ((int)v) {
        case 0: return WGPUCompareFunction_Undefined;
        case 1: return WGPUCompareFunction_Never;
        case 2: return WGPUCompareFunction_Less;
        case 3: return WGPUCompareFunction_LessEqual;
        case 4: return WGPUCompareFunction_Greater;
        case 5: return WGPUCompareFunction_GreaterEqual;
        case 6: return WGPUCompareFunction_Equal;
        case 7: return WGPUCompareFunction_NotEqual;
        case 8: return WGPUCompareFunction_Always;
        default: return WGPUCompareFunction_Undefined;
    }
}

static WGPUVertexStepMode mana_remap_vertex_step_mode(WGPUVertexStepMode v) {
    // Silk.NET: Vertex=0, Instance=1, VertexBufferNotUsed=2 (no Undefined).
    // Native:   Undefined=0, VertexBufferNotUsed=1, Vertex=2, Instance=3.
    switch ((int)v) {
        case 0: return WGPUVertexStepMode_Vertex;
        case 1: return WGPUVertexStepMode_Instance;
        case 2: return WGPUVertexStepMode_VertexBufferNotUsed;
        default: return WGPUVertexStepMode_Undefined;
    }
}

size_t mana_wgpuAdapterEnumerateFeatures(WGPUAdapter adapter, WGPUFeatureName * features) {
    return wgpuAdapterEnumerateFeatures(adapter, features);
}

WGPUBool mana_wgpuAdapterGetLimits(WGPUAdapter adapter, WGPUSupportedLimits * limits) {
    return wgpuAdapterGetLimits(adapter, limits);
}

void mana_wgpuAdapterGetProperties(WGPUAdapter adapter, WGPUAdapterProperties * properties) {
    wgpuAdapterGetProperties(adapter, properties);
}

void mana_wgpuAdapterRelease(WGPUAdapter adapter) {
    wgpuAdapterRelease(adapter);
}

void mana_wgpuAdapterRequestDevice(WGPUAdapter adapter, WGPUDeviceDescriptor * descriptor, WGPURequestDeviceCallback callback, void* userdata) {
    wgpuAdapterRequestDevice(adapter, descriptor, callback, userdata);
}

void mana_wgpuBindGroupLayoutRelease(WGPUBindGroupLayout bindGroupLayout) {
    wgpuBindGroupLayoutRelease(bindGroupLayout);
}

void mana_wgpuBindGroupRelease(WGPUBindGroup bindGroup) {
    wgpuBindGroupRelease(bindGroup);
}

void mana_wgpuBufferDestroy(WGPUBuffer buffer) {
    wgpuBufferDestroy(buffer);
}

void mana_wgpuBufferMapAsync(WGPUBuffer buffer, WGPUMapMode mode, size_t offset, size_t size, WGPUBufferMapCallback callback, void* userdata) {
    wgpuBufferMapAsync(buffer, mode, offset, size, callback, userdata);
}

void mana_wgpuBufferRelease(WGPUBuffer buffer) {
    wgpuBufferRelease(buffer);
}

void mana_wgpuCommandBufferRelease(WGPUCommandBuffer commandBuffer) {
    wgpuCommandBufferRelease(commandBuffer);
}

WGPUComputePassEncoder mana_wgpuCommandEncoderBeginComputePass(WGPUCommandEncoder commandEncoder, WGPUComputePassDescriptor * descriptor) {
    return wgpuCommandEncoderBeginComputePass(commandEncoder, descriptor);
}

WGPURenderPassEncoder mana_wgpuCommandEncoderBeginRenderPass(WGPUCommandEncoder commandEncoder, WGPURenderPassDescriptor * descriptor) {
    // No call site in this codebase sets RenderPassColorAttachment.DepthSlice explicitly, so it
    // is always the struct's default 0 -- a valid slice index, which the spec (and this
    // browser's real Dawn, strictly) requires be WGPU_DEPTH_SLICE_UNDEFINED instead whenever the
    // attachment's view isn't a 3D-texture slice, which none of ours are. Confirmed live: "0 is
    // defined for a non-3D attachment" render-pass validation error without this. wgpu-native
    // (desktop) evidently tolerates the 0 default; the browser does not.
    if (descriptor != NULL) {
        for (size_t i = 0; i < descriptor->colorAttachmentCount; i++) {
            ((WGPURenderPassColorAttachment*)descriptor->colorAttachments)[i].depthSlice = WGPU_DEPTH_SLICE_UNDEFINED;
        }
    }
    return wgpuCommandEncoderBeginRenderPass(commandEncoder, descriptor);
}

void mana_wgpuCommandEncoderCopyBufferToBuffer(WGPUCommandEncoder commandEncoder, WGPUBuffer source, uint64_t sourceOffset, WGPUBuffer destination, uint64_t destinationOffset, uint64_t size) {
    wgpuCommandEncoderCopyBufferToBuffer(commandEncoder, source, sourceOffset, destination, destinationOffset, size);
}

void mana_wgpuCommandEncoderCopyTextureToTexture(WGPUCommandEncoder commandEncoder, WGPUImageCopyTexture * source, WGPUImageCopyTexture * destination, WGPUExtent3D * copySize) {
    wgpuCommandEncoderCopyTextureToTexture(commandEncoder, source, destination, copySize);
}

WGPUCommandBuffer mana_wgpuCommandEncoderFinish(WGPUCommandEncoder commandEncoder, WGPUCommandBufferDescriptor * descriptor) {
    return wgpuCommandEncoderFinish(commandEncoder, descriptor);
}

void mana_wgpuCommandEncoderRelease(WGPUCommandEncoder commandEncoder) {
    wgpuCommandEncoderRelease(commandEncoder);
}

void mana_wgpuComputePassEncoderDispatchWorkgroups(WGPUComputePassEncoder computePassEncoder, uint32_t workgroupCountX, uint32_t workgroupCountY, uint32_t workgroupCountZ) {
    wgpuComputePassEncoderDispatchWorkgroups(computePassEncoder, workgroupCountX, workgroupCountY, workgroupCountZ);
}

void mana_wgpuComputePassEncoderEnd(WGPUComputePassEncoder computePassEncoder) {
    wgpuComputePassEncoderEnd(computePassEncoder);
}

void mana_wgpuComputePassEncoderRelease(WGPUComputePassEncoder computePassEncoder) {
    wgpuComputePassEncoderRelease(computePassEncoder);
}

void mana_wgpuComputePassEncoderSetBindGroup(WGPUComputePassEncoder computePassEncoder, uint32_t groupIndex, WGPUBindGroup group, size_t dynamicOffsetCount, uint32_t * dynamicOffsets) {
    wgpuComputePassEncoderSetBindGroup(computePassEncoder, groupIndex, group, dynamicOffsetCount, dynamicOffsets);
}

void mana_wgpuComputePassEncoderSetPipeline(WGPUComputePassEncoder computePassEncoder, WGPUComputePipeline pipeline) {
    wgpuComputePassEncoderSetPipeline(computePassEncoder, pipeline);
}

void mana_wgpuComputePipelineRelease(WGPUComputePipeline computePipeline) {
    wgpuComputePipelineRelease(computePipeline);
}

WGPUInstance mana_wgpuCreateInstance(WGPUInstanceDescriptor * descriptor) {
    return wgpuCreateInstance(descriptor);
}

// Not part of the mechanically-generated 69 (that list came from grepping ManaGraphics'
// existing call sites, and ManaBrowserWebGpuSurface.CreateFromCanvas was still a throwaway
// stub at generation time). Proven in the M0 spike; see ManaBrowserWebGpuSurface.cs.
WGPUSurface mana_wgpuInstanceCreateSurface(WGPUInstance instance, WGPUSurfaceDescriptor const* descriptor) {
    return wgpuInstanceCreateSurface(instance, descriptor);
}

WGPUBindGroup mana_wgpuDeviceCreateBindGroup(WGPUDevice device, WGPUBindGroupDescriptor * descriptor) {
    return wgpuDeviceCreateBindGroup(device, descriptor);
}

WGPUBindGroupLayout mana_wgpuDeviceCreateBindGroupLayout(WGPUDevice device, WGPUBindGroupLayoutDescriptor * descriptor) {
    return wgpuDeviceCreateBindGroupLayout(device, descriptor);
}

WGPUBuffer mana_wgpuDeviceCreateBuffer(WGPUDevice device, WGPUBufferDescriptor * descriptor) {
    return wgpuDeviceCreateBuffer(device, descriptor);
}

WGPUCommandEncoder mana_wgpuDeviceCreateCommandEncoder(WGPUDevice device, WGPUCommandEncoderDescriptor * descriptor) {
    return wgpuDeviceCreateCommandEncoder(device, descriptor);
}

WGPUComputePipeline mana_wgpuDeviceCreateComputePipeline(WGPUDevice device, WGPUComputePipelineDescriptor * descriptor) {
    return wgpuDeviceCreateComputePipeline(device, descriptor);
}

WGPUPipelineLayout mana_wgpuDeviceCreatePipelineLayout(WGPUDevice device, WGPUPipelineLayoutDescriptor * descriptor) {
    return wgpuDeviceCreatePipelineLayout(device, descriptor);
}

WGPURenderPipeline mana_wgpuDeviceCreateRenderPipeline(WGPUDevice device, WGPURenderPipelineDescriptor * descriptor) {
    if (descriptor != NULL) {
        WGPUVertexBufferLayout* buffers = (WGPUVertexBufferLayout*)descriptor->vertex.buffers;
        for (size_t i = 0; i < descriptor->vertex.bufferCount; i++) {
            buffers[i].stepMode = mana_remap_vertex_step_mode(buffers[i].stepMode);
        }

        descriptor->primitive.topology = mana_remap_primitive_topology(descriptor->primitive.topology);
        descriptor->primitive.frontFace = mana_remap_front_face(descriptor->primitive.frontFace);
        descriptor->primitive.cullMode = mana_remap_cull_mode(descriptor->primitive.cullMode);

        if (descriptor->depthStencil != NULL) {
            WGPUDepthStencilState* depthStencil = (WGPUDepthStencilState*)descriptor->depthStencil;
            depthStencil->depthCompare = mana_remap_compare_function(depthStencil->depthCompare);
            depthStencil->stencilFront.compare = mana_remap_compare_function(depthStencil->stencilFront.compare);
            depthStencil->stencilFront.failOp = mana_remap_stencil_operation(depthStencil->stencilFront.failOp);
            depthStencil->stencilFront.depthFailOp = mana_remap_stencil_operation(depthStencil->stencilFront.depthFailOp);
            depthStencil->stencilFront.passOp = mana_remap_stencil_operation(depthStencil->stencilFront.passOp);
            depthStencil->stencilBack.compare = mana_remap_compare_function(depthStencil->stencilBack.compare);
            depthStencil->stencilBack.failOp = mana_remap_stencil_operation(depthStencil->stencilBack.failOp);
            depthStencil->stencilBack.depthFailOp = mana_remap_stencil_operation(depthStencil->stencilBack.depthFailOp);
            depthStencil->stencilBack.passOp = mana_remap_stencil_operation(depthStencil->stencilBack.passOp);
        }

        if (descriptor->fragment != NULL) {
            WGPUFragmentState* fragment = (WGPUFragmentState*)descriptor->fragment;
            WGPUColorTargetState* targets = (WGPUColorTargetState*)fragment->targets;
            for (size_t i = 0; i < fragment->targetCount; i++) {
                if (targets[i].blend != NULL) {
                    WGPUBlendState* blend = (WGPUBlendState*)targets[i].blend;
                    blend->color.operation = mana_remap_blend_operation(blend->color.operation);
                    blend->color.srcFactor = mana_remap_blend_factor(blend->color.srcFactor);
                    blend->color.dstFactor = mana_remap_blend_factor(blend->color.dstFactor);
                    blend->alpha.operation = mana_remap_blend_operation(blend->alpha.operation);
                    blend->alpha.srcFactor = mana_remap_blend_factor(blend->alpha.srcFactor);
                    blend->alpha.dstFactor = mana_remap_blend_factor(blend->alpha.dstFactor);
                }
            }
        }
    }
    return wgpuDeviceCreateRenderPipeline(device, descriptor);
}

WGPUSampler mana_wgpuDeviceCreateSampler(WGPUDevice device, WGPUSamplerDescriptor * descriptor) {
    if (descriptor != NULL) {
        descriptor->addressModeU = mana_remap_address_mode(descriptor->addressModeU);
        descriptor->addressModeV = mana_remap_address_mode(descriptor->addressModeV);
        descriptor->addressModeW = mana_remap_address_mode(descriptor->addressModeW);
        descriptor->magFilter = mana_remap_filter_mode(descriptor->magFilter);
        descriptor->minFilter = mana_remap_filter_mode(descriptor->minFilter);
        descriptor->mipmapFilter = mana_remap_mipmap_filter_mode(descriptor->mipmapFilter);
        descriptor->compare = mana_remap_compare_function(descriptor->compare);
    }
    return wgpuDeviceCreateSampler(device, descriptor);
}

WGPUShaderModule mana_wgpuDeviceCreateShaderModule(WGPUDevice device, WGPUShaderModuleDescriptor * descriptor) {
    return wgpuDeviceCreateShaderModule(device, descriptor);
}

WGPUTexture mana_wgpuDeviceCreateTexture(WGPUDevice device, WGPUTextureDescriptor * descriptor) {
    // Silk.NET.WebGPU's TextureDimension enum has no Undefined member (TextureDimension1D=0,
    // TextureDimension2D=1, TextureDimension3D=2), but this Emscripten SDK's bundled webgpu.h
    // is an older/different header revision that does (WGPUTextureDimension_Undefined=0,
    // _1D=1, _2D=2, _3D=3) -- so a raw value Silk.NET wrote meaning "2D" (1) is read natively
    // as "1D" (1), which is exactly the "TextureDimension::e1D" validation error seen live in
    // the browser console for what should have been a 2D render-attachment texture. Only this
    // field is known to be shifted (TextureViewDimension already matches natively); remap it
    // here rather than in every caller.
    descriptor->dimension = (WGPUTextureDimension)((int)descriptor->dimension + 1);
    return wgpuDeviceCreateTexture(device, descriptor);
}

size_t mana_wgpuDeviceEnumerateFeatures(WGPUDevice device, WGPUFeatureName * features) {
    return wgpuDeviceEnumerateFeatures(device, features);
}

WGPUBool mana_wgpuDeviceGetLimits(WGPUDevice device, WGPUSupportedLimits * limits) {
    return wgpuDeviceGetLimits(device, limits);
}

WGPUQueue mana_wgpuDeviceGetQueue(WGPUDevice device) {
    return wgpuDeviceGetQueue(device);
}

void mana_wgpuDeviceRelease(WGPUDevice device) {
    wgpuDeviceRelease(device);
}

void mana_wgpuDeviceSetUncapturedErrorCallback(WGPUDevice device, WGPUErrorCallback callback, void* userdata) {
    wgpuDeviceSetUncapturedErrorCallback(device, callback, userdata);
}

void mana_wgpuInstanceProcessEvents(WGPUInstance instance) {
    wgpuInstanceProcessEvents(instance);
}

void mana_wgpuInstanceRelease(WGPUInstance instance) {
    wgpuInstanceRelease(instance);
}

void mana_wgpuInstanceRequestAdapter(WGPUInstance instance, WGPURequestAdapterOptions * options, WGPURequestAdapterCallback callback, void* userdata) {
    wgpuInstanceRequestAdapter(instance, options, callback, userdata);
}

void mana_wgpuPipelineLayoutRelease(WGPUPipelineLayout pipelineLayout) {
    wgpuPipelineLayoutRelease(pipelineLayout);
}

void mana_wgpuQueueRelease(WGPUQueue queue) {
    wgpuQueueRelease(queue);
}

void mana_wgpuQueueSubmit(WGPUQueue queue, size_t commandCount, WGPUCommandBuffer * commands) {
    wgpuQueueSubmit(queue, commandCount, commands);
}

void mana_wgpuQueueWriteBuffer(WGPUQueue queue, WGPUBuffer buffer, uint64_t bufferOffset, void* data, size_t size) {
    wgpuQueueWriteBuffer(queue, buffer, bufferOffset, data, size);
}

void mana_wgpuQueueWriteTexture(WGPUQueue queue, WGPUImageCopyTexture * destination, void* data, size_t dataSize, WGPUTextureDataLayout * dataLayout, WGPUExtent3D * writeSize) {
    wgpuQueueWriteTexture(queue, destination, data, dataSize, dataLayout, writeSize);
}

void mana_wgpuRenderPassEncoderDraw(WGPURenderPassEncoder renderPassEncoder, uint32_t vertexCount, uint32_t instanceCount, uint32_t firstVertex, uint32_t firstInstance) {
    wgpuRenderPassEncoderDraw(renderPassEncoder, vertexCount, instanceCount, firstVertex, firstInstance);
}

void mana_wgpuRenderPassEncoderDrawIndexed(WGPURenderPassEncoder renderPassEncoder, uint32_t indexCount, uint32_t instanceCount, uint32_t firstIndex, int32_t baseVertex, uint32_t firstInstance) {
    wgpuRenderPassEncoderDrawIndexed(renderPassEncoder, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
}

void mana_wgpuRenderPassEncoderEnd(WGPURenderPassEncoder renderPassEncoder) {
    wgpuRenderPassEncoderEnd(renderPassEncoder);
}

void mana_wgpuRenderPassEncoderRelease(WGPURenderPassEncoder renderPassEncoder) {
    wgpuRenderPassEncoderRelease(renderPassEncoder);
}

void mana_wgpuRenderPassEncoderSetBindGroup(WGPURenderPassEncoder renderPassEncoder, uint32_t groupIndex, WGPUBindGroup group, size_t dynamicOffsetCount, uint32_t * dynamicOffsets) {
    wgpuRenderPassEncoderSetBindGroup(renderPassEncoder, groupIndex, group, dynamicOffsetCount, dynamicOffsets);
}

void mana_wgpuRenderPassEncoderSetIndexBuffer(WGPURenderPassEncoder renderPassEncoder, WGPUBuffer buffer, WGPUIndexFormat format, uint64_t offset, uint64_t size) {
    wgpuRenderPassEncoderSetIndexBuffer(renderPassEncoder, buffer, format, offset, size);
}

void mana_wgpuRenderPassEncoderSetPipeline(WGPURenderPassEncoder renderPassEncoder, WGPURenderPipeline pipeline) {
    wgpuRenderPassEncoderSetPipeline(renderPassEncoder, pipeline);
}

void mana_wgpuRenderPassEncoderSetScissorRect(WGPURenderPassEncoder renderPassEncoder, uint32_t x, uint32_t y, uint32_t width, uint32_t height) {
    wgpuRenderPassEncoderSetScissorRect(renderPassEncoder, x, y, width, height);
}

void mana_wgpuRenderPassEncoderSetVertexBuffer(WGPURenderPassEncoder renderPassEncoder, uint32_t slot, WGPUBuffer buffer, uint64_t offset, uint64_t size) {
    wgpuRenderPassEncoderSetVertexBuffer(renderPassEncoder, slot, buffer, offset, size);
}

void mana_wgpuRenderPipelineRelease(WGPURenderPipeline renderPipeline) {
    wgpuRenderPipelineRelease(renderPipeline);
}

void mana_wgpuSamplerRelease(WGPUSampler sampler) {
    wgpuSamplerRelease(sampler);
}

void mana_wgpuShaderModuleRelease(WGPUShaderModule shaderModule) {
    wgpuShaderModuleRelease(shaderModule);
}

// Emscripten's bundled webgpu.h (the old -sUSE_WEBGPU=1 API; see WebPort-Plan.md M0/M2) predates
// the WebGPU spec's Surface-configure API (SurfaceConfigure/GetCurrentTexture/Present/Unconfigure,
// WGPUSurfaceConfiguration, WGPUSurfaceTexture) that Silk.NET.WebGPU (and ManaWebGpuContext.cs) is
// generated against; it only has the older explicit-swapchain shape (wgpuDeviceCreateSwapChain /
// wgpuSwapChainGetCurrentTexture / wgpuSwapChainPresent). Confirmed missing by an actual failed
// build (WGPUSurfaceConfiguration/WGPUSurfaceTexture: unknown type name), not assumed. These four
// adapt the modern Surface shape ManaWebGpuContext.cs already calls onto the legacy SwapChain
// underneath, so nothing on the C# side needs to change. The struct layouts below are hand-copied
// from Silk.NET.WebGPU's SurfaceConfiguration/SurfaceTexture field order (reflection-verified) --
// Emscripten's header has no such types to include, so there's nothing to check them against
// beyond that reflection dump; not yet confirmed correct against a real running browser.
typedef struct {
    void const* nextInChain;
    WGPUDevice device;
    WGPUTextureFormat format;
    WGPUTextureUsageFlags usage;
    size_t viewFormatCount;
    WGPUTextureFormat const* viewFormats;
    uint32_t alphaMode;
    uint32_t width;
    uint32_t height;
    WGPUPresentMode presentMode;
} ManaSurfaceConfiguration;

typedef struct {
    WGPUTexture texture;
    uint32_t suboptimal;
    uint32_t status;
} ManaSurfaceTexture;

static WGPUSwapChain mana_g_swapChain = 0;

void mana_wgpuSurfaceConfigure(WGPUSurface surface, ManaSurfaceConfiguration const* config) {
    if (mana_g_swapChain) {
        wgpuSwapChainRelease(mana_g_swapChain);
        mana_g_swapChain = 0;
    }
    WGPUSwapChainDescriptor scDesc = {0};
    scDesc.usage = config->usage;
    scDesc.format = config->format;
    scDesc.width = config->width;
    scDesc.height = config->height;
    scDesc.presentMode = config->presentMode;
    mana_g_swapChain = wgpuDeviceCreateSwapChain(config->device, surface, &scDesc);
}

void mana_wgpuSurfaceGetCurrentTexture(WGPUSurface surface, ManaSurfaceTexture* surfaceTexture) {
    surfaceTexture->texture = mana_g_swapChain ? wgpuSwapChainGetCurrentTexture(mana_g_swapChain) : 0;
    surfaceTexture->suboptimal = 0;
    surfaceTexture->status = surfaceTexture->texture ? 0 /* Success */ : 5 /* DeviceLost */;
}

WGPUTextureFormat mana_wgpuSurfaceGetPreferredFormat(WGPUSurface surface, WGPUAdapter adapter) {
    return wgpuSurfaceGetPreferredFormat(surface, adapter);
}

void mana_wgpuSurfacePresent(WGPUSurface surface) {
    // library_webgpu.js deliberately aborts wgpuSwapChainPresent ("unsupported, use
    // requestAnimationFrame via html5.h instead") -- confirmed live in the browser console,
    // not a theoretical concern. A browser canvas WebGPU context has no explicit present step:
    // whatever was drawn to it is shown automatically once the current requestAnimationFrame
    // callback returns control to the browser, which is exactly how mana_interop.js's render
    // loop is already structured. So there is nothing to do here in the browser build.
}

void mana_wgpuSurfaceRelease(WGPUSurface surface) {
    wgpuSurfaceRelease(surface);
}

void mana_wgpuSurfaceUnconfigure(WGPUSurface surface) {
    if (mana_g_swapChain) {
        wgpuSwapChainRelease(mana_g_swapChain);
        mana_g_swapChain = 0;
    }
}

WGPUTextureView mana_wgpuTextureCreateView(WGPUTexture texture, WGPUTextureViewDescriptor * descriptor) {
    // Same ABI drift as mana_wgpuDeviceCreateTexture's dimension remap, this time for
    // TextureAspect: Silk.NET's enum has no Undefined member (All=0, StencilOnly=1,
    // DepthOnly=2), but this SDK's native webgpu.h has one first (Undefined=0, All=1,
    // StencilOnly=2, DepthOnly=3) -- so Silk.NET's DepthOnly (2) was read natively as
    // StencilOnly (2), confirmed live by the browser's "does not have the texture view's
    // selected aspect (TextureAspect::StencilOnly)" validation error for a Depth24Plus
    // (no-stencil) view whose C# call site actually requested TextureAspect.DepthOnly.
    if (descriptor != NULL) {
        descriptor->aspect = (WGPUTextureAspect)((int)descriptor->aspect + 1);
    }
    return wgpuTextureCreateView(texture, descriptor);
}

void mana_wgpuTextureDestroy(WGPUTexture texture) {
    wgpuTextureDestroy(texture);
}

void mana_wgpuTextureRelease(WGPUTexture texture) {
    wgpuTextureRelease(texture);
}

void mana_wgpuTextureViewRelease(WGPUTextureView textureView) {
    wgpuTextureViewRelease(textureView);
}
