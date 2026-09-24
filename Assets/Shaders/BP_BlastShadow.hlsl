// Battle Plan — what a raised shield slab takes away from an explosion's picture of itself.
// An explosion's flat layers — the dust front, the burning rim, the flash, the burn left in the
// deck — are quads laid on the deck and centred on the blast, and none of them know that something
// is standing in the way. A slab takes away two different things, and both have to be answered or
// the shield does not read as being there at all:
//
//   REACH — the ground past the slab's own plane, inside the angle its width subtends from the
//   blast. Nothing displaces or burns there because the blast never arrives.
//
//   SIGHT — the ground the slab's own body hides from the camera. This is the one that makes or
//   breaks the read. Every one of these layers sits in the transparent queue ABOVE the shield's
//   material and writes no depth, so without this they paint straight across the standing slab and
//   the shockwave appears to run through the shield as though the board were empty. The slab is a
//   third opaque, so this attenuates rather than deletes: what is left is the ring seen dimly
//   through the barrier, which is what a translucent slab in front of a blast actually looks like.
//
// Both are the same ray test — is the slab between these two points, inside its width — so they
// share SlabCross. Resolved per pixel rather than as an angular span, which keeps it exact for a
// blast standing right against the slab, where the subtended angle is most of the circle and an
// angle-based test loses its precision entirely.
//   Values arrive in WORLD metres through SetVector, so one set of numbers serves every layer of a
//   burst whatever each layer's own quad is scaled or turned to. Feed them with BlastShadow.Apply.
//   Slab count 0 costs one scalar compare and returns daylight, so unshadowed bursts pay nothing.
#ifndef BP_BLAST_SHADOW_INCLUDED
#define BP_BLAST_SHADOW_INCLUDED

// The five uniforms below are declared by each shader inside its own CBUFFER_START(UnityPerMaterial)
// block, which is what keeps these layers on the SRP batcher, and this file is included after that
// block closes. Neither the declarations nor the ShaderLab properties can live here: the batcher
// needs them in the material's own constant buffer, and the Properties block is parsed before any
// include is resolved.
//   float4 _BlastShadow;     // xy blast origin (world xz), z slab count, w edge feather (m)
//   float4 _BlastSlab0;      // xy slab centre (world xz), zw slab plane normal (world xz)
//   float4 _BlastSlabEdge0;  // xy slab width axis (world xz), z half width, w top edge height (m)
//   float4 _BlastSlab1;
//   float4 _BlastSlabEdge1;

// How much of a layer the slab's body takes out of the frame where it stands in front of it. Nearly
// all of it: the shield's own material is only a third opaque, so anything left of a blast behind it
// still comes through the glass at full strength and the barrier stops reading as one at all.
#define BP_SLAB_OPACITY 0.94

/// Where the segment between two ground points crosses the slab plane, as a fraction of the way
/// from `from` to `ground`, or -1 when it never does inside the slab's width. `within` comes back
/// as how squarely the crossing sits inside that width, feathered at the ends.
float SlabCross(float2 ground, float2 from, float4 slab, float4 edge, float feather, out float within)
{
    within = 0.0;
    float2 ray = ground - from;
    float denom = dot(slab.zw, ray);
    if (abs(denom) < 1e-5)
        return -1.0;

    float travel = dot(slab.zw, slab.xy - from) / denom;
    if (travel <= 0.0 || travel >= 1.0)
        return -1.0;

    float2 crossing = from + ray * travel;
    float lateral = abs(dot(crossing - slab.xy, edge.xy));
    within = 1.0 - smoothstep(edge.z - feather, edge.z, lateral);
    return travel;
}

/// 1 where the blast reaches this point, 0 where the slab stands between them. The blast sits on
/// the deck, so the ray climbs from the ground to whatever height the layer is at: matter thrown
/// high enough to pass over the top edge is not in the slab's shadow and keeps burning.
float SlabReach(float3 positionWS, float2 origin, float4 slab, float4 edge, float feather)
{
    float2 ground = positionWS.xz;
    float within;
    float travel = SlabCross(ground, origin, slab, edge, feather, within);
    if (travel < 0.0)
        return 1.0;

    float under = 1.0 - smoothstep(edge.w - feather, edge.w, positionWS.y * travel);

    // Distance from the slab to the pixel, so material banked against the slab's foot still draws
    // and the shadow opens up behind it rather than starting at full depth on the slab's own line.
    float behind = smoothstep(0.0, feather, (1.0 - travel) * length(ground - origin));
    return 1.0 - within * behind * under;
}

/// 1 where the camera can see this ground, BP_SLAB_OPACITY down where the slab's body is in front
/// of it. The ray runs from the eye down to the pixel, so the slab only hides what the ray passes
/// under: ground far enough back to be seen over the top edge is left alone.
float SlabSight(float3 positionWS, float4 slab, float4 edge, float feather)
{
    float3 eye = _WorldSpaceCameraPos;
    float within;
    float travel = SlabCross(positionWS.xz, eye.xz, slab, edge, feather, within);
    if (travel < 0.0)
        return 1.0;

    float height = lerp(eye.y, positionWS.y, travel);
    float under = 1.0 - smoothstep(edge.w - feather, edge.w, height);
    return 1.0 - within * under * BP_SLAB_OPACITY;
}

/// How much of this pixel's layer survives every slab standing in the blast.
float BlastShadow(float3 positionWS)
{
    float slabs = _BlastShadow.z;
    if (slabs < 0.5)
        return 1.0;

    float2 origin = _BlastShadow.xy;
    float feather = max(_BlastShadow.w, 1e-3);

    float mask = min(
        SlabReach(positionWS, origin, _BlastSlab0, _BlastSlabEdge0, feather),
        SlabSight(positionWS, _BlastSlab0, _BlastSlabEdge0, feather)
    );
    if (slabs > 1.5)
    {
        mask = min(mask, SlabReach(positionWS, origin, _BlastSlab1, _BlastSlabEdge1, feather));
        mask = min(mask, SlabSight(positionWS, _BlastSlab1, _BlastSlabEdge1, feather));
    }
    return mask;
}

#endif
