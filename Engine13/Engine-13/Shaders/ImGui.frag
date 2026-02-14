#version 450
layout(location = 0) in vec2 fsin_TexCoord;
layout(location = 1) in vec4 fsin_Colour;
// Use separate texture and sampler bindings to match Veldrid ResourceLayout (texture at binding=1, sampler at binding=2)
layout(set = 0, binding = 1) uniform texture2D FontTexture;
layout(set = 0, binding = 2) uniform sampler FontSampler;
layout(location = 0) out vec4 fsout_Colour;
void main() { vec4 sampled = texture(sampler2D(FontTexture, FontSampler), fsin_TexCoord); fsout_Colour = fsin_Colour * sampled; }