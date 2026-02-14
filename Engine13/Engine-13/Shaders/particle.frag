#version 450

// Input from vertex shader
layout(location = 0) in vec4 fragColour;
layout(location = 1) in vec2 fragUV;

// Output colour
layout(location = 0) out vec4 fsout_Colour;

void main()
{
    // fragUV is normalized: ranges from -1 to +1, center is (0,0)
    float dist = length(fragUV);
    
    // Discard pixels outside the circle
    if (dist > 1.0)
        discard;
    
    // Outline: outer 20% of the circle is the outline
    float outlineStart = 0.8;
    vec4 outlineColour = vec4(0.0, 0.0, 0.0, 1.0);  // Black
    
    if (dist > outlineStart)
        fsout_Colour = outlineColour;
    else
        fsout_Colour = fragColour;
}