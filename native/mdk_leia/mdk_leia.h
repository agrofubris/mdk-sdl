/* mdk_leia: the MDK port's Simulated Reality (Leia SR) host, a C ABI shim the C# side loads.
 * Built with MDK_LEIA_SR it links SR-lib / the SR SDK (delay-loaded; see build.ps1); without
 * it every call declines and the game presents the plain side-by-side pair.
 * Ported from agrofubris/starfox-enhanced-stereo (src/app/leia_sr_host.hpp, GPL-3.0), which
 * follows oneup03's RT64 3D design and bo3b's SR-lib. The proprietary Simulated Reality SDK
 * and its runtime stay with Leia Inc. and the user; none of it is distributed here. */
#pragma once
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Creates the weaver once for the device and window; 0 when the SR runtime, the service or an
 * SR display is missing (latched: later calls answer 0 without retrying). release() re-arms. */
__declspec(dllexport) int mdk_leia_ensure(void* d3d12_device, void* hwnd);
__declspec(dllexport) int mdk_leia_available(void);

/* The SDL weave bridge's callback: records the SR weave into the command list the bridge has
 * already bound the target on. Must not submit or close the list, and must not throw across
 * the C boundary. */
__declspec(dllexport) int mdk_leia_weave(void* user, void* command_list, void* native_source,
    uint32_t width, uint32_t height, uint32_t output_format);

/* Drops the weaver; call before the device or any texture it sampled goes. release() re-arms
 * the probe for a new device; disable() latches the side-by-side fallback for this one. */
__declspec(dllexport) void mdk_leia_release(void);
__declspec(dllexport) void mdk_leia_disable(void);

#ifdef __cplusplus
}
#endif
