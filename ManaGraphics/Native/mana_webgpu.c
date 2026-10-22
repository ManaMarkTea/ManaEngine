#include <webgpu/webgpu.h>
#include <stddef.h>
#include <stdint.h>
#include <emscripten/em_js.h>

// Bypasses library_webgpu.js's own wgpuInstanceRequestAdapter/wgpuAdapterRequestDevice
// entirely for these two calls: their callback-delivery path (a `dynCall`/getWasmTableEntry
// invocation of the C# callback function pointer) hits a confirmed Emscripten runtime bug
// (RuntimeError: getWasmTableEntry(...) is not a function) when combined with .NET's wasm
// bundling -- reproducible, not fixed by AOT compilation, not fixed by explicitly forcing
// getWasmTableEntry past tree-shaking. See the browser build notes in AGENTS.md.
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
EM_JS(void, mana_request_adapter_impl, (int requestId), {
    if (!('gpu' in navigator)) {
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserWebGpuCallbackBridge.OnAdapterResult(requestId, 0);
        }
        return;
    }
    // The fast GPU on a laptop that has two: without asking, Windows may hand the browser the built-in one.
    navigator.gpu.requestAdapter({ powerPreference: 'high-performance' }).then((adapter) => {
        if (adapter) {
            // Which GPU the page got -- the first thing to know when a machine runs slowly.
            var info = adapter.info || {};
            var fallback = info.isFallbackAdapter || adapter.isFallbackAdapter;
            var described = [info.vendor, info.architecture, info.device, info.description].filter(Boolean).join(' / ') || 'unknown GPU';
            if (fallback) described += ' (software fallback: drawn on the CPU, very slow)';
            console.log('[WebGPU] adapter: ' + described);
            // Kept for the engine (gpuAdapter in mana_interop.js) and the page's info mark.
            globalThis.ManaGpuAdapter = described;
            window.dispatchEvent(new CustomEvent('mana-gpu', { detail: described }));
        }
        var adapterId = adapter ? WebGPU.mgrAdapter.create(adapter) : 0;
        var accepted = globalThis.ManaExports &&
            globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserWebGpuCallbackBridge.OnAdapterResult(requestId, adapterId);
        if (adapterId && !accepted) WebGPU.mgrAdapter.release(adapterId);
    }, (ex) => {
        console.error('mana_request_adapter failed', ex);
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserWebGpuCallbackBridge.OnAdapterResult(requestId, 0);
        }
    });
});

EM_JS(void, mana_request_device_impl, (int adapterId, int requestId), {
    var adapter = WebGPU.mgrAdapter.get(adapterId);
    adapter.requestDevice().then((device) => {
        // Mirrors library_webgpu.js's own wgpuAdapterRequestDevice exactly: DeviceGetQueue
        // later reads WebGPU.mgrDevice.objects[deviceId].queueId, so the default queue has to
        // be pre-registered into the device's wrapper object at creation time, not lazily.
        var deviceWrapper = { queueId: WebGPU.mgrQueue.create(device.queue) };
        var deviceId = WebGPU.mgrDevice.create(device, deviceWrapper);
        var accepted = globalThis.ManaExports &&
            globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserWebGpuCallbackBridge.OnDeviceResult(requestId, deviceId);
        if (!accepted) {
            device.destroy();
            WebGPU.mgrQueue.release(deviceWrapper.queueId);
            WebGPU.mgrDevice.release(deviceId);
        }
    }, (ex) => {
        console.error('mana_request_device failed', ex);
        if (globalThis.ManaExports) {
            globalThis.ManaExports.ManaGraphics.Code.Graphics.Browser.ManaBrowserWebGpuCallbackBridge.OnDeviceResult(requestId, 0);
        }
    });
});

void mana_request_adapter(int requestId) {
    mana_request_adapter_impl(requestId);
}

void mana_request_device(int adapterId, int requestId) {
    mana_request_device_impl(adapterId, requestId);
}

// Diagnostic only, not part of the real render path: WebGPU's wgpuShaderModuleGetCompilationInfo
// is itself a callback-taking async call and would hit the exact same getWasmTableEntry bug the
// adapter/device requests did if called through Silk.NET's normal binding, so this reaches
// library_webgpu.js's own shader-module id table (WebGPU.mgrShaderModule, confirmed present by
// reading src/library_webgpu.js in this Emscripten install) directly via EM_JS instead, and just
// logs the result -- there is no consumer on the C# side, nothing to round-trip. Added to
// investigate why the demo mesh pipeline's shader draws nothing despite a clean render pipeline,
// zero WebGPU validation errors, and the mesh/uniform data all confirmed present; Dawn's
// validation-error console logging (confirmed unconditional and independent of any registered
// error callback -- see the TextureAspect findings) turned out NOT to cover WGSL compilation
// results, which are reported through this separate, opt-in API instead.
EM_JS(void, mana_log_shader_compilation_info_impl, (int shaderModuleId), {
    var shaderModule = WebGPU.mgrShaderModule.get(shaderModuleId);
    if (!shaderModule) {
        console.error('mana_log_shader_compilation_info: no shader module for id', shaderModuleId);
        return;
    }
    shaderModule.getCompilationInfo().then((info) => {
        if (info.messages.length === 0) {
            console.log('[ShaderCompilationInfo] shader module ' + shaderModuleId + ': no messages.');
            return;
        }
        for (var i = 0; i < info.messages.length; i++) {
            var m = info.messages[i];
            var text = '[ShaderCompilationInfo] shader module ' + shaderModuleId + ' (' + m.type + ') line ' + m.lineNum + ':' + m.linePos + ': ' + m.message;
            if (m.type === 'error') {
                console.error(text);
            } else if (m.type === 'warning') {
                console.warn(text);
            } else {
                console.log(text);
            }
        }
    }, (ex) => {
        console.error('mana_log_shader_compilation_info: getCompilationInfo rejected', ex);
    });
});

void mana_log_shader_compilation_info(int shaderModuleId) {
    mana_log_shader_compilation_info_impl(shaderModuleId);
}

// Diagnostic only: a direct GPU-side readback of a buffer's actual bytes, to confirm a
// QueueWriteBuffer upload really landed rather than continuing to infer it from C# source
// reading. wgpuBufferMapAsync is disabled for browser (same getWasmTableEntry callback bug as
// DeviceSetUncapturedErrorCallback), and the source buffer is UNIFORM|COPY_DST anyway (not
// MAP_READ-compatible per spec even if it worked), so this copies it into a throwaway
// MAP_READ|COPY_DST staging buffer first, the standard WebGPU readback pattern, then maps and
// logs that instead. Prints as float32s since every uniform struct in this codebase (frame and
// object) is entirely f32/vec4/mat4 fields.
EM_JS(void, mana_debug_readback_buffer_impl, (int deviceId, int sourceBufferId, int byteSize), {
    var device = WebGPU.mgrDevice.get(deviceId);
    var sourceBuffer = WebGPU.mgrBuffer.get(sourceBufferId);
    var stagingBuffer = device.createBuffer({
        size: byteSize,
        usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST
    });
    var encoder = device.createCommandEncoder();
    encoder.copyBufferToBuffer(sourceBuffer, 0, stagingBuffer, 0, byteSize);
    device.queue.submit([encoder.finish()]);
    stagingBuffer.mapAsync(GPUMapMode.READ).then(() => {
        var floats = new Float32Array(stagingBuffer.getMappedRange().slice(0));
        console.log('[BufferReadback] buffer ' + sourceBufferId + ' (' + byteSize + ' bytes): [' + Array.from(floats).join(', ') + ']');
        stagingBuffer.unmap();
        stagingBuffer.destroy();
    }, (ex) => {
        console.error('[BufferReadback] mapAsync failed for buffer ' + sourceBufferId, ex);
    });
});

void mana_debug_readback_buffer(int deviceId, int sourceBufferId, int byteSize) {
    mana_debug_readback_buffer_impl(deviceId, sourceBufferId, byteSize);
}

// ---- Enum ABI remapping between Silk.NET.WebGPU 2.23.0 and this Emscripten SDK's bundled
// webgpu.h ----
// Both target structurally identical structs (every struct relevant to the render pipeline
// path was checked field-for-field against Silk.NET.WebGPU.dll decompiled via ilspycmd: same
// order, same shape, no insertions/removals), but many enums were renumbered between the two
// webgpu.h revisions -- discovered by mechanically diffing every Silk.NET.WebGPU enum's
// declared ordinals against this exact installed header. TextureDimension and TextureAspect
// are remapped in their individual shim functions because each has only one affected field.
// Most of the rest are a uniform "add 1" (native inserted an Undefined=0 member Silk.NET's
// enum doesn't have, shifting everything after it by exactly one -- confirmed uniform across
// every member, not just the ones that happened to collide). Three
// (AddressMode/CompareFunction/VertexStepMode) are genuinely reordered, not just shifted, and
// need an explicit table. The mappings are exercised by the browser WebGPU render path; compare
// them against the bundled header whenever the Emscripten or Silk.NET version changes.
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

// Configures the canvas directly rather than going through Emscripten's legacy
// wgpuDeviceCreateSwapChain, for one reason: viewFormats.
//
// WebGPU requires a canvas be configured with navigator.gpu.getPreferredCanvasFormat(), which is
// always a non-sRGB format -- Dawn rejects the -srgb variant outright. But it DOES allow listing
// the -srgb variant in viewFormats and rendering into a view of that format, which gets the
// automatic linear-to-sRGB transfer on write that desktop's Srgb surface provides for free.
// Emscripten's swapchain path predates viewFormats and cannot express it, so everything drawing
// straight to the surface (the tone-map pass, ImGui, ManaUI) came out about a gamma too dark.
//
// Doing it here rather than encoding in each shader also keeps blending correct: with a real sRGB
// view the GPU decodes, blends in linear, and re-encodes, exactly as desktop does. Hand-encoding
// in the shaders would blend already-encoded values and quietly change every UI alpha result.
EM_JS(void, mana_configure_surface_impl, (int surfaceId, int deviceId, int formatInt, int usage), {
    var context = WebGPU.mgrSurface.get(surfaceId);
    var device = WebGPU.mgrDevice.get(deviceId);
    var format = WebGPU.TextureFormat[formatInt];

    // C# hands us the sRGB format it builds pipelines and views against; the canvas itself has to
    // be configured with the plain variant, so strip the suffix for the format and keep the sRGB
    // one as the view format.
    var base = format.endsWith('-srgb') ? format.slice(0, -'-srgb'.length) : format;
    var viewFormats = base === format ? [] : [format];

    context.configure({
        device: device,
        format: base,
        viewFormats: viewFormats,
        // The canvas defaults to RenderAttachment only; the UI blur copies the frame out (CopySrc).
        usage: usage,
        alphaMode: 'opaque'
    });
});

EM_JS(int, mana_surface_get_current_texture_impl, (int surfaceId), {
    var context = WebGPU.mgrSurface.get(surfaceId);
    if (!context) {
        return 0;
    }
    return WebGPU.mgrTexture.create(context.getCurrentTexture());
});

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

// Generous caps for the render-pipeline scratch-copy arrays below -- this is a hobby game
// engine's pipelines, not a AAA renderer's; comfortably above anything GetOrCreatePipeline/
// CreateDemoRenderPipeline/CreateHdrPostProcessor/etc. actually construct. Deliberately a hard
// cap with a console warning rather than a dynamic allocation: pipeline creation is rare
// (cached per shader name), not a hot path, so simplicity wins over avoiding a fixed size.
#define MANA_MAX_VERTEX_BUFFERS 8
#define MANA_MAX_COLOR_TARGETS 8

WGPURenderPipeline mana_wgpuDeviceCreateRenderPipeline(WGPUDevice device, WGPURenderPipelineDescriptor * descriptor) {
    // Every remap below runs on LOCAL COPIES, never the caller's own descriptor/array/struct
    // memory -- see mana_wgpuDeviceCreateSampler's comment for why in-place mutation is unsafe
    // in this codebase (copy-and-tweak descriptor reuse silently double-remaps on a second use).
    // RenderPipelineDescriptor's vertex.buffers/depthStencil/fragment/fragment->targets/
    // target.blend are all POINTERS the caller owns, so a shallow top-level copy alone isn't
    // enough (it copies the pointer VALUE, not what it points to) -- each pointed-to
    // array/struct that needs a field remapped gets its own scratch copy here, and the local
    // top-level descriptor's pointer fields are redirected at those scratch copies instead.
    if (descriptor == NULL) {
        return wgpuDeviceCreateRenderPipeline(device, descriptor);
    }

    WGPURenderPipelineDescriptor local = *descriptor;

    WGPUVertexBufferLayout bufferScratch[MANA_MAX_VERTEX_BUFFERS];
    size_t bufferCount = local.vertex.bufferCount;
    if (bufferCount > MANA_MAX_VERTEX_BUFFERS) {
        bufferCount = MANA_MAX_VERTEX_BUFFERS;
    }
    if (local.vertex.buffers != NULL && bufferCount > 0) {
        for (size_t i = 0; i < bufferCount; i++) {
            bufferScratch[i] = local.vertex.buffers[i];
            bufferScratch[i].stepMode = mana_remap_vertex_step_mode(bufferScratch[i].stepMode);
        }
        local.vertex.buffers = bufferScratch;
        local.vertex.bufferCount = bufferCount;
    }

    local.primitive.topology = mana_remap_primitive_topology(local.primitive.topology);
    local.primitive.frontFace = mana_remap_front_face(local.primitive.frontFace);
    local.primitive.cullMode = mana_remap_cull_mode(local.primitive.cullMode);

    WGPUDepthStencilState depthStencilScratch;
    if (local.depthStencil != NULL) {
        depthStencilScratch = *local.depthStencil;
        depthStencilScratch.depthCompare = mana_remap_compare_function(depthStencilScratch.depthCompare);
        depthStencilScratch.stencilFront.compare = mana_remap_compare_function(depthStencilScratch.stencilFront.compare);
        depthStencilScratch.stencilFront.failOp = mana_remap_stencil_operation(depthStencilScratch.stencilFront.failOp);
        depthStencilScratch.stencilFront.depthFailOp = mana_remap_stencil_operation(depthStencilScratch.stencilFront.depthFailOp);
        depthStencilScratch.stencilFront.passOp = mana_remap_stencil_operation(depthStencilScratch.stencilFront.passOp);
        depthStencilScratch.stencilBack.compare = mana_remap_compare_function(depthStencilScratch.stencilBack.compare);
        depthStencilScratch.stencilBack.failOp = mana_remap_stencil_operation(depthStencilScratch.stencilBack.failOp);
        depthStencilScratch.stencilBack.depthFailOp = mana_remap_stencil_operation(depthStencilScratch.stencilBack.depthFailOp);
        depthStencilScratch.stencilBack.passOp = mana_remap_stencil_operation(depthStencilScratch.stencilBack.passOp);
        local.depthStencil = &depthStencilScratch;
    }

    WGPUFragmentState fragmentScratch;
    WGPUColorTargetState targetScratch[MANA_MAX_COLOR_TARGETS];
    WGPUBlendState blendScratch[MANA_MAX_COLOR_TARGETS];
    if (local.fragment != NULL) {
        fragmentScratch = *local.fragment;
        size_t targetCount = fragmentScratch.targetCount;
        if (targetCount > MANA_MAX_COLOR_TARGETS) {
            targetCount = MANA_MAX_COLOR_TARGETS;
        }
        if (fragmentScratch.targets != NULL && targetCount > 0) {
            for (size_t i = 0; i < targetCount; i++) {
                targetScratch[i] = fragmentScratch.targets[i];
                if (targetScratch[i].blend != NULL) {
                    blendScratch[i] = *targetScratch[i].blend;
                    blendScratch[i].color.operation = mana_remap_blend_operation(blendScratch[i].color.operation);
                    blendScratch[i].color.srcFactor = mana_remap_blend_factor(blendScratch[i].color.srcFactor);
                    blendScratch[i].color.dstFactor = mana_remap_blend_factor(blendScratch[i].color.dstFactor);
                    blendScratch[i].alpha.operation = mana_remap_blend_operation(blendScratch[i].alpha.operation);
                    blendScratch[i].alpha.srcFactor = mana_remap_blend_factor(blendScratch[i].alpha.srcFactor);
                    blendScratch[i].alpha.dstFactor = mana_remap_blend_factor(blendScratch[i].alpha.dstFactor);
                    targetScratch[i].blend = &blendScratch[i];
                }
            }
            fragmentScratch.targets = targetScratch;
            fragmentScratch.targetCount = targetCount;
        }
        local.fragment = &fragmentScratch;
    }

    return wgpuDeviceCreateRenderPipeline(device, &local);
}

WGPUSampler mana_wgpuDeviceCreateSampler(WGPUDevice device, WGPUSamplerDescriptor * descriptor) {
    // Remap on a LOCAL COPY, never the caller's own descriptor memory: C# call sites routinely
    // copy-and-tweak a descriptor for a second use (e.g. `var x = someDescriptor; x.Format =
    // ...;`), so mutating *descriptor in place would leave the caller's own struct holding an
    // already-remapped (native-numbered) value. A second call built from that copy would then
    // remap it AGAIN, silently producing a different, wrong enum value. Confirmed live: this
    // exact bug, for mana_wgpuTextureCreateView's old in-place version, turned a legitimate
    // TextureAspect.All view into TextureAspect.StencilOnly on the second use of a copied
    // descriptor. Keep the per-enum mappings above aligned with the installed header.
    if (descriptor == NULL) {
        return wgpuDeviceCreateSampler(device, descriptor);
    }
    WGPUSamplerDescriptor local = *descriptor;
    local.addressModeU = mana_remap_address_mode(local.addressModeU);
    local.addressModeV = mana_remap_address_mode(local.addressModeV);
    local.addressModeW = mana_remap_address_mode(local.addressModeW);
    local.magFilter = mana_remap_filter_mode(local.magFilter);
    local.minFilter = mana_remap_filter_mode(local.minFilter);
    local.mipmapFilter = mana_remap_mipmap_filter_mode(local.mipmapFilter);
    local.compare = mana_remap_compare_function(local.compare);
    return wgpuDeviceCreateSampler(device, &local);
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
    //
    // Remapped on a LOCAL COPY, not *descriptor itself -- see mana_wgpuDeviceCreateSampler's
    // comment for why in-place mutation of the caller's own descriptor memory is unsafe here
    // (copy-and-tweak reuse, e.g. `var x = textureDescriptor; x.Format = ...;`, would otherwise
    // silently double-remap on the second use).
    if (descriptor == NULL) {
        return wgpuDeviceCreateTexture(device, descriptor);
    }
    WGPUTextureDescriptor local = *descriptor;
    local.dimension = (WGPUTextureDimension)((int)local.dimension + 1);
    return wgpuDeviceCreateTexture(device, &local);
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

// This SDK's manager does not release the queue reference created with its device wrapper.
EM_JS(void, mana_release_device_impl, (int deviceId), {
    var wrapper = WebGPU.mgrDevice.objects[deviceId];
    if (wrapper.refcount === 1 && wrapper.queueId) WebGPU.mgrQueue.release(wrapper.queueId);
    WebGPU.mgrDevice.release(deviceId);
});

void mana_wgpuDeviceRelease(WGPUDevice device) {
    mana_release_device_impl((int)(uintptr_t)device);
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

// Emscripten's bundled webgpu.h (the old -sUSE_WEBGPU=1 API) predates
// the WebGPU spec's Surface-configure API (SurfaceConfigure/GetCurrentTexture/Present/Unconfigure,
// WGPUSurfaceConfiguration, WGPUSurfaceTexture) that Silk.NET.WebGPU (and ManaWebGpuContext.cs) is
// generated against; it only has the older explicit-swapchain shape (wgpuDeviceCreateSwapChain /
// wgpuSwapChainGetCurrentTexture / wgpuSwapChainPresent). Confirmed missing by an actual failed
// build (WGPUSurfaceConfiguration/WGPUSurfaceTexture: unknown type name), not assumed. These four
// adapt the modern Surface shape ManaWebGpuContext.cs already calls onto the legacy SwapChain
// underneath, so nothing on the C# side needs to change. The struct layouts below are hand-copied
// from Silk.NET.WebGPU's SurfaceConfiguration/SurfaceTexture field order (reflection-verified) --
// Emscripten's header has no such types to include. This adapter is exercised by the browser
// renderer.
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

void mana_wgpuSurfaceConfigure(WGPUSurface surface, ManaSurfaceConfiguration const* config) {
    mana_configure_surface_impl((int)(intptr_t)surface, (int)(intptr_t)config->device, (int)config->format, (int)config->usage);
}

void mana_wgpuSurfaceGetCurrentTexture(WGPUSurface surface, ManaSurfaceTexture* surfaceTexture) {
    int textureId = mana_surface_get_current_texture_impl((int)(intptr_t)surface);
    surfaceTexture->texture = (WGPUTexture)(intptr_t)textureId;
    surfaceTexture->suboptimal = 0;
    surfaceTexture->status = textureId ? 0 /* Success */ : 5 /* DeviceLost */;
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
    // Nothing to release: the canvas context is configured in place (see
    // mana_configure_surface_impl) rather than owning a swapchain object, and re-configuring it
    // simply replaces the previous configuration.
}

WGPUTextureView mana_wgpuTextureCreateView(WGPUTexture texture, WGPUTextureViewDescriptor * descriptor) {
    // Same ABI drift as mana_wgpuDeviceCreateTexture's dimension remap, this time for
    // TextureAspect: Silk.NET's enum has no Undefined member (All=0, StencilOnly=1,
    // DepthOnly=2), but this SDK's native webgpu.h has one first (Undefined=0, All=1,
    // StencilOnly=2, DepthOnly=3) -- so Silk.NET's DepthOnly (2) was read natively as
    // StencilOnly (2), confirmed live by the browser's "does not have the texture view's
    // selected aspect (TextureAspect::StencilOnly)" validation error for a Depth24Plus
    // (no-stencil) view whose C# call site actually requested TextureAspect.DepthOnly.
    //
    // Remapped on a LOCAL COPY, not *descriptor itself -- mutating the caller's own descriptor
    // memory in place is unsafe here: this codebase copy-and-tweaks a TextureViewDescriptor for
    // a second view (`var x = viewDescriptor; x.Format = ...;`), so an in-place +1 here would
    // leave the caller's original struct holding the already-remapped native value, and the
    // COPY built from it would then get remapped a second time -- turning a legitimate
    // TextureAspect.All (Silk 0 -> native 1 -> would-be native 2) into TextureAspect.StencilOnly
    // on that second view. Confirmed live via a self-driven headless-Chrome probe (Playwright)
    // after enabling the demo mesh pipeline: exactly this double-remap produced spurious
    // "selected aspect (TextureAspect::StencilOnly)" errors on plain RGBA8Unorm/RGBA16Float
    // color targets that every C# call site actually requests TextureAspect.All for.
    if (descriptor == NULL) {
        return wgpuTextureCreateView(texture, descriptor);
    }
    WGPUTextureViewDescriptor local = *descriptor;
    local.aspect = (WGPUTextureAspect)((int)local.aspect + 1);
    return wgpuTextureCreateView(texture, &local);
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
