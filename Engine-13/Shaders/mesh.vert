#version 450

// Per-mesh position uniform
layout(set = 0, binding = 0) uniform PositionBuffer {
    vec2 meshPosition;
};

// Projection matrix (orthographic)
layout(set = 1, binding = 0) uniform ProjectionBuffer {
    mat4 u_Projection;
};

// Vertex input
layout(location = 0) in vec2 inPosition;

// Output to fragment shader
layout(location = 0) out vec2 fragUV;

void main()
{
    vec4 worldPos = vec4(inPosition + meshPosition, 0.0, 1.0);
    gl_Position = u_Projection * worldPos;
    fragUV = inPosition;
}