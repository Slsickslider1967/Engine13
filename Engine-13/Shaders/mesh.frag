#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 1) in vec2 fsin_UV;

layout(location = 0) out vec4 fsout_Color;

void main()
{
    vec2 center = fsin_UV - vec2(0.5);
    float dist = length(center) * 2.0;
    
    if (dist > 1.0)
        discard;
    
    float outlineStart = 0.75;
    vec4 outlineColor = vec4(0.0, 0.0, 0.0, 1.0);
    
    if (dist > outlineStart)
        fsout_Color = outlineColor;
    else
        fsout_Color = fsin_Color;
}