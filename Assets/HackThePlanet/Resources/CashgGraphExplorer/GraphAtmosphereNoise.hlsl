#ifndef GRAPH_ATMOSPHERE_NOISE_INCLUDED
#define GRAPH_ATMOSPHERE_NOISE_INCLUDED

float GraphHash(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float GraphNoise(float3 p)
{
    float3 cell = floor(p);
    float3 blend = frac(p);
    blend = blend * blend * (3.0 - 2.0 * blend);
    float n000 = GraphHash(cell);
    float n100 = GraphHash(cell + float3(1, 0, 0));
    float n010 = GraphHash(cell + float3(0, 1, 0));
    float n110 = GraphHash(cell + float3(1, 1, 0));
    float n001 = GraphHash(cell + float3(0, 0, 1));
    float n101 = GraphHash(cell + float3(1, 0, 1));
    float n011 = GraphHash(cell + float3(0, 1, 1));
    float n111 = GraphHash(cell + float3(1, 1, 1));
    float x00 = lerp(n000, n100, blend.x);
    float x10 = lerp(n010, n110, blend.x);
    float x01 = lerp(n001, n101, blend.x);
    float x11 = lerp(n011, n111, blend.x);
    return lerp(lerp(x00, x10, blend.y), lerp(x01, x11, blend.y), blend.z);
}

float GraphFbm(float3 p)
{
    float value = 0.0;
    float amplitude = 0.5;
    [unroll]
    for (int octave = 0; octave < 4; octave++)
    {
        value += amplitude * GraphNoise(p);
        p = p * 2.03 + float3(1.7, 9.2, 3.1);
        amplitude *= 0.5;
    }
    return value;
}

#endif

