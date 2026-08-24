#include <webgpu/webgpu.h>
#include <stddef.h>
#include <stdint.h>

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
    return wgpuDeviceCreateRenderPipeline(device, descriptor);
}

WGPUSampler mana_wgpuDeviceCreateSampler(WGPUDevice device, WGPUSamplerDescriptor * descriptor) {
    return wgpuDeviceCreateSampler(device, descriptor);
}

WGPUShaderModule mana_wgpuDeviceCreateShaderModule(WGPUDevice device, WGPUShaderModuleDescriptor * descriptor) {
    return wgpuDeviceCreateShaderModule(device, descriptor);
}

WGPUTexture mana_wgpuDeviceCreateTexture(WGPUDevice device, WGPUTextureDescriptor * descriptor) {
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
    if (mana_g_swapChain) {
        wgpuSwapChainPresent(mana_g_swapChain);
    }
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
