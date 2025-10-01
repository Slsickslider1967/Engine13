#version 450

// Per-mesh color at set=2, binding=0
layout(set = 2, binding = 0) uniform ColorBuffer {
    vec4 u_Color;
};

layout(location = 0) out vec4 fsout_Color;
void main()
{
    fsout_Color = u_Color;
}
