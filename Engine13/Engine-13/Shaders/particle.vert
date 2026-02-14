#version 450

// Projection matrix (orthographic)
layout(set = 0, binding = 0) uniform ProjectionBuffer {
    mat4 u_Projection;
};

// Per-vertex input (circle geometry)
layout(location = 0) in vec2 inPosition;

// Per-instance input (particle data)
layout(location = 1) in vec2 inInstancePos;
layout(location = 2) in vec4 inInstanceColour;

// Output to fragment shader
layout(location = 0) out vec4 fragColour;
layout(location = 1) out vec2 fragUV;

void main()
{
    vec4 worldPos = vec4(inPosition + inInstancePos, 0.0, 1.0);
    gl_Position = u_Projection * worldPos;
    fragColour = inInstanceColour;
    
    // Normalize UV: inPosition goes from -radius to +radius
    // We need to find the radius (max distance from center in this vertex batch)
    // For a circle, we can normalize by the length of the position
    // Center vertex is (0,0), edge vertices have length = radius
    float radius = 0.005;  // Matches Sand particle size
    fragUV = inPosition / radius;  // Now ranges from -1 to +1
}