#ifndef VFX_OCCLUSION_INCLUDED
#define VFX_OCCLUSION_INCLUDED

// Ensure multi_compile variants are generated for the depth keywords
#pragma multi_compile _ SOFT_OCCLUSION
#pragma multi_compile _ HARD_OCCLUSION

// Include Meta XR depth utilities (absolute path, skipped in SG preview)
#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
#endif

/// Returns 1.0 when the fragment is visible (not occluded by real-world geometry).
/// Returns 0.0 when the fragment is occluded.
/// In Unity Editor without Quest 3 hardware, always returns 1.0 (particles visible).
void VFXOcclusion_float(float3 WorldPos, out float OcclusionValue)
{
#ifdef SHADERGRAPH_PREVIEW
    OcclusionValue = 1.0f;
#else
    OcclusionValue = CalculateEnvironmentDepthOcclusion(WorldPos, 0.0f);
#endif
}

void VFXOcclusion_half(half3 WorldPos, out half OcclusionValue)
{
    float result;
    VFXOcclusion_float(float3(WorldPos), result);
    OcclusionValue = half(result);
}

#endif // VFX_OCCLUSION_INCLUDED
