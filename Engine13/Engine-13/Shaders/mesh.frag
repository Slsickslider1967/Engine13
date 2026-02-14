#version 450

// Colour uniform buffer
layout(set = 2, binding = 0) uniform ColourBuffer {
    vec4 meshColour;
};

// Input from vertex shader
layout(location = 0) in vec2 fsin_UV;

// Output colour
layout(location = 0) out vec4 fsout_Colour;

void main()
{
    vec2 center = fsin_UV - vec2(0.5);
    float dist = length(center) * 2.0;
    
    if (dist > 1.0)
        discard;
    
    float outlineStart = 0.75;
    vec4 outlineColour = vec4(0.0, 0.0, 0.0, 1.0);
    
    if (dist > outlineStart)
        fsout_Colour = outlineColour;
    else
        fsout_Colour = meshColour;
}