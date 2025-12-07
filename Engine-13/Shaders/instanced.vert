#version 450

layout(set = 0, binding = 0) uniform ProjectionBuffer {
    mat4 u_Projection;
};

layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inInstancePos;
layout(location = 2) in vec4 inInstanceColor;

layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 worldPos = vec4(inPosition + inInstancePos, 0.0, 1.0);
    gl_Position = u_Projection * worldPos;
    fragColor = inInstanceColor;
}
