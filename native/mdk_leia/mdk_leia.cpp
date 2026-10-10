// The MDK port's Simulated Reality host (see mdk_leia.h). The weaver is created lazily on the
// present thread, fed the packed side-by-side texture every frame, and dropped before the
// device. A missing runtime, service or display leaves it unavailable: the caller presents the
// same un-woven pair. Ported from agrofubris/starfox-enhanced-stereo src/app/leia_sr_host.hpp
// (GPL-3.0), which follows oneup03's RT64 3D and bo3b's SR-lib.

#include "mdk_leia.h"

#include <cstdint>
#include <cstdio>

#if defined(MDK_LEIA_SR)

#include <windows.h>
#include <d3d12.h>
#include <cstdio>
#include <exception>
#include "SR.hpp"

namespace
{
    SimulatedReality::SRInterfaceDX12 *weaver = nullptr;
    bool unavailable = false;
    std::uint32_t output_format = 0;

    // Delete() destroys the weaver and then the context, in that order; the interface itself is
    // never `delete`d. It can throw when the device was lost under the weaver: never let
    // teardown escape.
    void drop() noexcept
    {
        if (weaver != nullptr)
        {
            try { weaver->Delete(); } catch (...) {}
            weaver = nullptr;
        }

        output_format = 0;
    }
}

extern "C" int mdk_leia_ensure(void *d3d12_device, void *hwnd)
{
    if (weaver != nullptr)
    {
        return 1;
    }

    if (unavailable || d3d12_device == nullptr || hwnd == nullptr)
    {
        unavailable = true;
        return 0;
    }

    SimulatedReality::SRInterfaceDX12 *sr = nullptr;
    HRESULT hr = E_FAIL;
    try
    {
        hr = SimulatedReality::CreateSRInterfaceDX12(
            static_cast<ID3D12Device *>(d3d12_device), static_cast<HWND>(hwnd), &sr);
    }
    catch (const std::exception &error)
    {
        // The SR runtime is optional and lives outside the app: a throw here must degrade to
        // the side-by-side fallback, never abort.
        std::fprintf(stderr, "leia-sr: CreateSRInterfaceDX12 threw: %s; presenting side-by-side\n", error.what());
        unavailable = true;
        return 0;
    }
    catch (...)
    {
        std::fprintf(stderr, "leia-sr: CreateSRInterfaceDX12 threw; presenting side-by-side\n");
        unavailable = true;
        return 0;
    }

    if (FAILED(hr) || sr == nullptr)
    {
        unavailable = true;
        std::fprintf(stderr, "leia-sr: CreateSRInterfaceDX12 failed (hr=0x%08lx); presenting side-by-side\n",
            static_cast<unsigned long>(hr));
        return 0;
    }

    weaver = sr;
    // The packed intermediate and the panel's surface are both UNORM and the compose already
    // encoded gamma, so the weaver must not convert in either direction.
    weaver->SetShaderSRGBConversion(false, false);
    std::fprintf(stderr, "leia-sr: weaver initialized\n");
    return 1;
}

extern "C" int mdk_leia_available(void)
{
    return weaver != nullptr;
}

extern "C" int mdk_leia_weave(void *user, void *command_list, void *native_source,
    std::uint32_t width, std::uint32_t height, std::uint32_t format)
{
    (void)user;
    if (weaver == nullptr || command_list == nullptr || native_source == nullptr || width == 0 || height == 0)
    {
        return 0;
    }

    try
    {
        // Rebind every frame: sizes and formats come off the resource desc, and a cached view
        // can go stale across a resize.
        weaver->SetInputTexture(static_cast<ID3D12Resource *>(native_source));
        if (format != output_format)
        {
            weaver->SetOutputFormat(static_cast<DXGI_FORMAT>(format));
            output_format = format;
        }

        const D3D12_VIEWPORT viewport{0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height), 0.0f, 1.0f};
        const D3D12_RECT scissor{0, 0, static_cast<LONG>(width), static_cast<LONG>(height)};
        weaver->Weave(static_cast<ID3D12GraphicsCommandList *>(command_list), viewport, scissor);
        return 1;
    }
    catch (const std::exception &error)
    {
        std::fprintf(stderr, "leia-sr: weave threw: %s\n", error.what());
        return 0;
    }
    catch (...)
    {
        std::fprintf(stderr, "leia-sr: weave threw\n");
        return 0;
    }
}

extern "C" void mdk_leia_release(void)
{
    drop();
    unavailable = false;
}

extern "C" void mdk_leia_disable(void)
{
    drop();
    unavailable = true;
}

#else // No SDK: the stub the build always compiles; the mode falls back to side by side.

extern "C" int mdk_leia_ensure(void *d3d12_device, void *hwnd)
{
    (void)d3d12_device;
    (void)hwnd;
    static bool logged = false;
    if (!logged)
    {
        logged = true;
        std::fprintf(stderr, "leia-sr: no SR SDK in this build (mdk_leia stub); presenting side-by-side\n");
    }

    return 0;
}

extern "C" int mdk_leia_available(void)
{
    return 0;
}

extern "C" int mdk_leia_weave(void *user, void *command_list, void *native_source,
    std::uint32_t width, std::uint32_t height, std::uint32_t format)
{
    (void)user;
    (void)command_list;
    (void)native_source;
    (void)width;
    (void)height;
    (void)format;
    return 0;
}

extern "C" void mdk_leia_release(void)
{
}

extern "C" void mdk_leia_disable(void)
{
}

#endif
