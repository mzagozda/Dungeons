shader_type spatial;
#include "res://shaders/pixel_sample.gdshaderinc"
render_mode blend_mix, cull_disabled, diffuse_lambert, specular_disabled;
uniform sampler2D albedo_atlas : source_color, filter_nearest_mipmap_anisotropic, repeat_disable;
uniform vec4 color : source_color = vec4(0.184, 0.435, 0.561, 0.7);
uniform vec2 speed = vec2(0.025, 0.012);
uniform vec4 atlas_rect;
varying vec2 ground;
global uniform float capture_time;
void vertex() {
    float animation_time = capture_time >= 0.0 ? capture_time : TIME;
    ground = VERTEX.xz;
    VERTEX.y += sin(animation_time * 1.5 + VERTEX.x * 1.2 + VERTEX.z * 0.7) * 0.03;
}
void fragment() {
    float animation_time = capture_time >= 0.0 ? capture_time : TIME;
    vec2 flowing_uv = atlas_rect.xy + fract(ground + animation_time * speed) * atlas_rect.zw;
    vec3 texel = pixel_sample(albedo_atlas, flowing_uv).rgb;
    float foam = COLOR.r * (0.24 + 0.08 * sin(animation_time * 2.0 + ground.x * 7.0));
    ALBEDO = mix(texel * color.rgb * 1.8, vec3(0.74, 0.87, 0.84), foam);
    ALPHA = color.a;
    ROUGHNESS = 0.35;
}
