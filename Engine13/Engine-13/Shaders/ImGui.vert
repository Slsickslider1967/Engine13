#version 450
layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in vec4 Colour;
layout(set = 0, binding = 0) uniform Projection { mat4 ProjectionMatrix; };
layout(location = 0) out vec2 fsin_TexCoord;
layout(location = 1) out vec4 fsin_Colour;
void main() { fsin_TexCoord = TexCoord; fsin_Colour = Colour; gl_Position = ProjectionMatrix * vec4(Position, 0, 1); }