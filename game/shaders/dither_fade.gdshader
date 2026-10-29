shader_type spatial;
#include "res://shaders/pixel_sample.gdshaderinc"
render_mode diffuse_burley, specular_disabled;
uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap_anisotropic, repeat_disable;
uniform float emission_strength = 0.0;
uniform float emission_multiplier = 1.0;
uniform bool instance_custom_emission = false;
uniform bool instance_custom_alpha = false;
uniform float dither_alpha : hint_range(0.0, 1.0) = 1.0;
varying float opacity;
varying float emission_scale;
void vertex() { opacity = instance_custom_alpha ? INSTANCE_CUSTOM.a : dither_alpha; emission_scale = instance_custom_emission ? INSTANCE_CUSTOM.r : emission_multiplier; }
void fragment() {
    // Four-by-four screen-door threshold: no alpha blend sorting between occluders.
    ivec2 p = ivec2(FRAGCOORD.xy) % 4;
    int index = p.x + p.y * 4;
    float thresholds[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
    if (opacity < (thresholds[index] + 0.5) / 16.0) { discard; }
    ALBEDO = pixel_sample(albedo_tex, UV).rgb * COLOR.rgb;
    EMISSION = ALBEDO * emission_strength * emission_scale;
    ROUGHNESS = 1.0;
}
