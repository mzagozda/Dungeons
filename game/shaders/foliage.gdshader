shader_type spatial;
#include "res://shaders/pixel_sample.gdshaderinc"
render_mode cull_disabled, unshaded;
uniform sampler2D albedo_atlas : source_color, filter_nearest_mipmap_anisotropic, repeat_disable;
uniform float sway_strength = 0.035;
uniform vec4 ambient_tint : source_color = vec4(0.7, 0.8, 0.9, 1.0);
global uniform float capture_time;
void vertex() {
    float animation_time = capture_time >= 0.0 ? capture_time : TIME;
    VERTEX.x += sin(animation_time * 1.8 + VERTEX.x * 0.9 + VERTEX.z) * sway_strength * COLOR.a;
}
void fragment() {
    vec4 texel = pixel_sample(albedo_atlas, UV);
    ALBEDO = texel.rgb * COLOR.rgb * ambient_tint.rgb;
    ALPHA = texel.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
    ROUGHNESS = 1.0;
}
