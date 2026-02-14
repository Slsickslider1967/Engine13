#version 450

// Color uniform buffer
layout(set = 2, binding = 0) uniform ColorBuffer {
    vec4 meshColor;
};

// Input from vertex shader
layout(location = 0) in vec2 fsin_UV;

// Output color
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
        fsout_Color = meshColor;
}