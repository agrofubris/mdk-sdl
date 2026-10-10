/* Included inside the pinned SDL D3D12 backend, after its private helpers. */
#include "sdl_d3d12_bridge.h"

static bool Starfox_D3D12_CopyNativeBuffer(void *command, void *source, void *destination, uint32_t bytes)
{
    D3D12CommandBuffer *cb = (D3D12CommandBuffer *)command;
    ID3D12Resource *resource = (ID3D12Resource *)source;
    D3D12BufferContainer *container = (D3D12BufferContainer *)destination;
    ID3D12Device *owner = NULL;
    D3D12_RESOURCE_DESC desc;
    D3D12_RESOURCE_BARRIER barrier;
    D3D12Buffer *buffer;
    HRESULT hr;
    if (!cb || !resource || !container || !bytes || bytes > container->size)
        return SDL_SetError("Invalid Starfox native buffer copy: command=%d source=%d destination=%d bytes=%u capacity=%u",
            cb != NULL, resource != NULL, container != NULL, bytes, container ? container->size : 0);
    hr = ID3D12Resource_GetDevice(resource, D3D_GUID(D3D_IID_ID3D12Device), (void **)&owner);
    if (FAILED(hr)) return SDL_SetError("Cannot identify native buffer device");
    if (owner != cb->renderer->device) {
        ID3D12Device_Release(owner);
        return SDL_SetError("Native buffer must be opened on the SDL device");
    }
    ID3D12Device_Release(owner);
    ID3D12Resource_GetDesc(resource, &desc);
    if (desc.Dimension != D3D12_RESOURCE_DIMENSION_BUFFER || desc.Width < bytes)
        return SDL_SetError("Native buffer is too small or not a buffer");
    buffer = D3D12_INTERNAL_PrepareBufferForWrite(cb, container, true, D3D12_RESOURCE_STATE_COPY_DEST);
    SDL_zero(barrier);
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = resource;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_SOURCE;
    ID3D12GraphicsCommandList_ResourceBarrier(cb->graphicsCommandList, 1, &barrier);
    ID3D12GraphicsCommandList_CopyBufferRegion(cb->graphicsCommandList, buffer->handle, 0, resource, 0, bytes);
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_SOURCE;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COMMON;
    ID3D12GraphicsCommandList_ResourceBarrier(cb->graphicsCommandList, 1, &barrier);
    D3D12_INTERNAL_BufferTransitionToDefaultUsage(cb, D3D12_RESOURCE_STATE_COPY_DEST, buffer);
    D3D12_INTERNAL_TrackBuffer(cb, buffer);
    return true;
}
static bool Starfox_D3D12_WaitFence(void *device, void *fence, uint64_t value)
{
    D3D12Renderer *renderer;
    HRESULT hr;
    if (!device || !fence || !value) return SDL_SetError("Invalid native producer fence");
    renderer = (D3D12Renderer *)((SDL_GPUDevice *)device)->driverData;
    SDL_LockMutex(renderer->submitLock);
    hr = ID3D12CommandQueue_Wait(renderer->commandQueue, (ID3D12Fence *)fence, value);
    SDL_UnlockMutex(renderer->submitLock);
    if (FAILED(hr)) return SDL_SetError("Native producer fence wait failed");
    return true;
}
static const StarfoxSdlD3D12BridgeV2 starfox_d3d12_bridge = {2, Starfox_D3D12_CopyNativeBuffer, Starfox_D3D12_WaitFence};

static bool Starfox_D3D12_CopyToExternal(void *command, void *source, void *destination, uint32_t bytes)
{
    D3D12CommandBuffer *cb = (D3D12CommandBuffer *)command;
    D3D12BufferContainer *container = (D3D12BufferContainer *)source;
    ID3D12Resource *resource = (ID3D12Resource *)destination;
    ID3D12Device *owner = NULL;
    D3D12_RESOURCE_DESC desc;
    D3D12_RESOURCE_BARRIER barrier;
    D3D12Buffer *buffer;
    if (!cb || !container || !resource || !bytes || (bytes & 3) || bytes > container->size)
        return SDL_SetError("Invalid Starfox geometry copy");
    if (FAILED(ID3D12Resource_GetDevice(resource, D3D_GUID(D3D_IID_ID3D12Device), (void **)&owner)))
        return SDL_SetError("Cannot identify geometry destination device");
    if (owner != cb->renderer->device) {
        ID3D12Device_Release(owner);
        return SDL_SetError("Geometry destination must be opened on SDL device");
    }
    ID3D12Device_Release(owner);
    ID3D12Resource_GetDesc(resource, &desc);
    if (desc.Dimension != D3D12_RESOURCE_DIMENSION_BUFFER || desc.Width < bytes)
        return SDL_SetError("Invalid geometry destination size");
    buffer = container->activeBuffer;
    D3D12_INTERNAL_BufferTransitionFromDefaultUsage(cb, D3D12_RESOURCE_STATE_COPY_SOURCE, buffer);
    SDL_zero(barrier);
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = resource;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
    ID3D12GraphicsCommandList_ResourceBarrier(cb->graphicsCommandList, 1, &barrier);
    ID3D12GraphicsCommandList_CopyBufferRegion(cb->graphicsCommandList, resource, 0, buffer->handle, 0, bytes);
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COMMON;
    ID3D12GraphicsCommandList_ResourceBarrier(cb->graphicsCommandList, 1, &barrier);
    D3D12_INTERNAL_BufferTransitionToDefaultUsage(cb, D3D12_RESOURCE_STATE_COPY_SOURCE, buffer);
    D3D12_INTERNAL_TrackBuffer(cb, buffer);
    return true;
}
static bool Starfox_D3D12_SignalFence(void *device, void *fence, uint64_t value)
{
    D3D12Renderer *renderer;
    HRESULT hr;
    if (!device || !fence || !value) return SDL_SetError("Invalid geometry completion fence");
    renderer = (D3D12Renderer *)((SDL_GPUDevice *)device)->driverData;
    SDL_LockMutex(renderer->submitLock);
    hr = ID3D12CommandQueue_Signal(renderer->commandQueue, (ID3D12Fence *)fence, value);
    SDL_UnlockMutex(renderer->submitLock);
    if (FAILED(hr)) return SDL_SetError("Geometry completion signal failed");
    return true;
}
static const StarfoxSdlD3D12GeometryBridgeV1 starfox_d3d12_geometry_bridge = {1, Starfox_D3D12_CopyToExternal, Starfox_D3D12_SignalFence};

static bool Starfox_D3D12_CopyTexture(void *command, void *sdl_texture, void *native_texture, bool importing)
{
    D3D12CommandBuffer *cb = (D3D12CommandBuffer *)command;
    D3D12TextureContainer *container = (D3D12TextureContainer *)sdl_texture;
    ID3D12Resource *external = (ID3D12Resource *)native_texture;
    ID3D12Device *owner = NULL;
    D3D12_RESOURCE_DESC a, b;
    D3D12_RESOURCE_BARRIER barrier;
    D3D12TextureSubresource *sub;
    D3D12_TEXTURE_COPY_LOCATION src, dst;
    D3D12_RESOURCE_STATES state = importing ? D3D12_RESOURCE_STATE_COPY_SOURCE : D3D12_RESOURCE_STATE_COPY_DEST;
    if (!cb || !container || !external || !container->activeTexture)
        return SDL_SetError("Invalid Starfox texture copy");
    if (FAILED(ID3D12Resource_GetDevice(external, D3D_GUID(D3D_IID_ID3D12Device), (void **)&owner)))
        return SDL_SetError("Cannot identify native texture device");
    if (owner != cb->renderer->device) {
        ID3D12Device_Release(owner);
        return SDL_SetError("Native texture belongs to another device");
    }
    ID3D12Device_Release(owner);
    ID3D12Resource_GetDesc(external, &a);
    ID3D12Resource_GetDesc(container->activeTexture->resource, &b);
    if (external == container->activeTexture->resource ||
        a.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D || a.Dimension != b.Dimension ||
        a.Width != b.Width || a.Height != b.Height || a.Format != b.Format ||
        a.DepthOrArraySize != 1 || b.DepthOrArraySize != 1 ||
        a.MipLevels != 1 || b.MipLevels != 1 || a.SampleDesc.Count != 1 || b.SampleDesc.Count != 1)
        return SDL_SetError("Native texture copy requires matching single-mip 2D textures");
    sub = importing ? D3D12_INTERNAL_PrepareTextureSubresourceForWrite(cb, container, 0, 0, true, D3D12_RESOURCE_STATE_COPY_DEST)
                    : D3D12_INTERNAL_FetchTextureSubresource(container, 0, 0);
    if (!importing) D3D12_INTERNAL_TextureSubresourceTransitionFromDefaultUsage(cb, D3D12_RESOURCE_STATE_COPY_SOURCE, sub);
    SDL_zero(barrier);
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = external;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
    barrier.Transition.StateAfter = state;
    ID3D12GraphicsCommandList_ResourceBarrier(cb->graphicsCommandList, 1, &barrier);
    SDL_zero(src); SDL_zero(dst);
    src.Type = dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    src.pResource = importing ? external : sub->parent->resource;
    dst.pResource = importing ? sub->parent->resource : external;
    ID3D12GraphicsCommandList_CopyTextureRegion(cb->graphicsCommandList, &dst, 0, 0, 0, &src, NULL);
    barrier.Transition.StateBefore = state;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COMMON;
    ID3D12GraphicsCommandList_ResourceBarrier(cb->graphicsCommandList, 1, &barrier);
    D3D12_INTERNAL_TextureSubresourceTransitionToDefaultUsage(cb, importing ? D3D12_RESOURCE_STATE_COPY_DEST : D3D12_RESOURCE_STATE_COPY_SOURCE, sub);
    D3D12_INTERNAL_TrackTexture(cb, sub->parent);
    return true;
}
static bool Starfox_D3D12_ExportTexture(void *command, void *texture, void *native_texture)
{ return Starfox_D3D12_CopyTexture(command, texture, native_texture, false); }
static bool Starfox_D3D12_ImportTexture(void *command, void *native_texture, void *texture)
{ return Starfox_D3D12_CopyTexture(command, texture, native_texture, true); }
static const StarfoxSdlD3D12TextureBridgeV1 starfox_d3d12_texture_bridge = {1, Starfox_D3D12_ExportTexture, Starfox_D3D12_ImportTexture};

static bool Starfox_D3D12_DispatchNative(void *command, void *const *textures, uint32_t count,
    uint32_t output_index, StarfoxD3D12ComputeCallback callback, void *user)
{
    D3D12CommandBuffer *cb = (D3D12CommandBuffer *)command;
    D3D12TextureSubresource *subresources[8];
    void *resources[8];
    bool result;
    if (!cb || !textures || !callback || !count || count > 8 || output_index >= count)
        return SDL_SetError("Invalid native compute dispatch");
    for (uint32_t i = 0; i < count; ++i) {
        D3D12TextureContainer *container = (D3D12TextureContainer *)textures[i];
        if (!container || !container->activeTexture || container->activeTexture->subresourceCount != 1 ||
            container->header.info.type != SDL_GPU_TEXTURETYPE_2D || container->header.info.sample_count != SDL_GPU_SAMPLECOUNT_1 ||
            (i == output_index && !(container->header.info.usage & SDL_GPU_TEXTUREUSAGE_COMPUTE_STORAGE_WRITE)))
            return SDL_SetError("Native compute requires single-mip 2D textures and a writable output");
        for (uint32_t j = 0; j < i; ++j)
            if (textures[j] == textures[i]) return SDL_SetError("Native compute texture alias");
    }
    for (uint32_t i = 0; i < count; ++i) {
        D3D12TextureContainer *container = (D3D12TextureContainer *)textures[i];
        if (i == output_index)
            subresources[i] = D3D12_INTERNAL_PrepareTextureSubresourceForWrite(cb, container, 0, 0, true, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        else {
            subresources[i] = D3D12_INTERNAL_FetchTextureSubresource(container, 0, 0);
            D3D12_INTERNAL_TextureSubresourceTransitionFromDefaultUsage(cb, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, subresources[i]);
        }
        resources[i] = subresources[i]->parent->resource;
        D3D12_INTERNAL_TrackTexture(cb, subresources[i]->parent);
    }
    result = callback(user, cb->graphicsCommandList, resources, count);
    for (uint32_t i = 0; i < count; ++i)
        D3D12_INTERNAL_TextureSubresourceTransitionToDefaultUsage(cb, i == output_index ? D3D12_RESOURCE_STATE_UNORDERED_ACCESS : D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, subresources[i]);
    if (cb->gpuDescriptorHeaps[0] && cb->gpuDescriptorHeaps[1]) {
        ID3D12DescriptorHeap *heaps[] = {cb->gpuDescriptorHeaps[0]->handle, cb->gpuDescriptorHeaps[1]->handle};
        ID3D12GraphicsCommandList_SetDescriptorHeaps(cb->graphicsCommandList, 2, heaps);
    }
    cb->currentComputePipeline = NULL;
    cb->currentGraphicsPipeline = NULL;
    return result;
}
static const StarfoxSdlD3D12ComputeBridgeV1 starfox_d3d12_compute_bridge = {1, Starfox_D3D12_DispatchNative};

static bool Starfox_D3D12_RestorePresentation(void *device)
{
    D3D12Renderer *renderer;
    const StarfoxSdlD3D12PresentHooksV1 *hooks = (const StarfoxSdlD3D12PresentHooksV1 *)SDL_GetPointerProperty(SDL_GetGlobalProperties(), STARFOX_SDL_D3D12_PRESENT_HOOKS, NULL);
    if (!device) return SDL_SetError("Invalid presentation device");
    if (!hooks || hooks->version != 1 || !hooks->swapchain) return true;
    renderer = (D3D12Renderer *)((SDL_GPUDevice *)device)->driverData;
    for (uint32_t i = 0; i < renderer->claimedWindowCount; ++i)
        if (!hooks->swapchain(hooks->user, renderer->device, (void **)&renderer->claimedWindows[i]->swapchain, true)) return false;
    return true;
}
static const StarfoxSdlD3D12PresentBridgeV1 starfox_d3d12_present_bridge = {1, Starfox_D3D12_RestorePresentation};

static void Starfox_D3D12_BindDescriptorHeaps(D3D12CommandBuffer *cb)
{
    if (cb->gpuDescriptorHeaps[D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV] &&
        cb->gpuDescriptorHeaps[D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER]) {
        ID3D12DescriptorHeap *heaps[2];
        heaps[0] = cb->gpuDescriptorHeaps[D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV]->handle;
        heaps[1] = cb->gpuDescriptorHeaps[D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER]->handle;
        ID3D12GraphicsCommandList_SetDescriptorHeaps(cb->graphicsCommandList, 2, heaps);
    }
}
static bool Starfox_D3D12_PresentWeave(void *command, void *source, void *swapchain_texture,
    uint32_t width, uint32_t height, StarfoxD3D12PresentWeaveCallback callback, void *user)
{
    D3D12CommandBuffer *cb = (D3D12CommandBuffer *)command;
    D3D12TextureContainer *source_container = (D3D12TextureContainer *)source;
    D3D12TextureContainer *target_container = (D3D12TextureContainer *)swapchain_texture;
    D3D12TextureSubresource *source_subresource;
    D3D12TextureSubresource *target_subresource;
    D3D12_CPU_DESCRIPTOR_HANDLE rtv;
    D3D12_VIEWPORT viewport;
    D3D12_RECT scissor;
    bool result;
    if (!cb || !source_container || !target_container || !callback || !width || !height ||
        !source_container->activeTexture || !target_container->activeTexture ||
        source_container->activeTexture->subresourceCount != 1 ||
        target_container->activeTexture->subresourceCount != 1 ||
        source_container->header.info.type != SDL_GPU_TEXTURETYPE_2D ||
        target_container->header.info.type != SDL_GPU_TEXTURETYPE_2D ||
        source_container->header.info.sample_count != SDL_GPU_SAMPLECOUNT_1 ||
        target_container->header.info.sample_count != SDL_GPU_SAMPLECOUNT_1)
        return SDL_SetError("Invalid Starfox present weave");
    if (source_container->activeTexture->resource == target_container->activeTexture->resource)
        return SDL_SetError("Present weave requires distinct textures");
    source_subresource = D3D12_INTERNAL_FetchTextureSubresource(source_container, 0, 0);
    D3D12_INTERNAL_TextureSubresourceTransitionFromDefaultUsage(cb, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, source_subresource);
    D3D12_INTERNAL_TrackTexture(cb, source_subresource->parent);
    // The target is an ordinary app-owned SDL texture, whose default state is
    // a shader resource. Transition it for the callback and back afterwards;
    // binding a freshly acquired swapchain here would skip that and is no
    // longer the supported path.
    target_subresource = D3D12_INTERNAL_FetchTextureSubresource(target_container, 0, 0);
    D3D12_INTERNAL_TextureSubresourceTransitionFromDefaultUsage(cb, D3D12_RESOURCE_STATE_RENDER_TARGET, target_subresource);
    D3D12_INTERNAL_TrackTexture(cb, target_subresource->parent);
    rtv = target_subresource->rtvHandles[0].cpuHandle;
    ID3D12GraphicsCommandList_OMSetRenderTargets(cb->graphicsCommandList, 1, &rtv, false, NULL);
    Starfox_D3D12_BindDescriptorHeaps(cb);
    SDL_zero(viewport);
    viewport.Width = (float)width;
    viewport.Height = (float)height;
    viewport.MaxDepth = 1.0f;
    scissor.left = 0;
    scissor.top = 0;
    scissor.right = (LONG)width;
    scissor.bottom = (LONG)height;
    ID3D12GraphicsCommandList_RSSetViewports(cb->graphicsCommandList, 1, &viewport);
    ID3D12GraphicsCommandList_RSSetScissorRects(cb->graphicsCommandList, 1, &scissor);
    result = callback(user, cb->graphicsCommandList, source_subresource->parent->resource,
        width, height, (uint32_t)SDLToD3D12_TextureFormat[target_container->header.info.format]);
    ID3D12GraphicsCommandList_OMSetRenderTargets(cb->graphicsCommandList, 0, NULL, false, NULL);
    D3D12_INTERNAL_TextureSubresourceTransitionToDefaultUsage(cb, D3D12_RESOURCE_STATE_RENDER_TARGET, target_subresource);
    D3D12_INTERNAL_TextureSubresourceTransitionToDefaultUsage(cb, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, source_subresource);
    Starfox_D3D12_BindDescriptorHeaps(cb);
    cb->currentGraphicsPipeline = NULL;
    cb->currentComputePipeline = NULL;
    return result;
}
static const StarfoxSdlD3D12PresentWeaveBridgeV1 starfox_d3d12_present_weave_bridge = {1, Starfox_D3D12_PresentWeave};
