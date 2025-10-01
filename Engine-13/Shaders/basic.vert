#version 450
// Example GLSL vertex shader
layout(set = 0, binding = 0) uniform PositionBuffer {
    vec2 meshPosition;
};

// New: projection matrix (orthographic) at set=1, binding=0
layout(set = 1, binding = 0) uniform ProjectionBuffer {
    mat4 u_Projection;
};

layout(location = 0) in vec2 inPosition;
layout(location = 0) out vec2 fragUV;

void main()
{
    vec4 local = vec4(inPosition + meshPosition, 0.0, 1.0);
    gl_Position = u_Projection * local;
    fragUV = inPosition;
}