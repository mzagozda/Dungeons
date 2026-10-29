shader_type spatial;
#include "res://shaders/pixel_sample.gdshaderinc"
render_mode diffuse_burley, specular_disabled;
uniform sampler2D albedo_atlas : source_color, filter_nearest_mipmap_anisotropic, repeat_disable;
uniform vec4 tint : source_color = vec4(1.0);
vec3 renderer_tint(vec3 linear_tint) {
    // Worker vertex colours are linear. Compatibility shades into an sRGB buffer.
    vec3 low = linear_tint * 12.92;
    vec3 high = 1.055 * pow(max(linear_tint, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055;
    return OUTPUT_IS_SRGB ? mix(high, low, lessThanEqual(linear_tint, vec3(0.0031308))) : linear_tint;
}
void fragment() {
    ALBEDO = pixel_sample(albedo_atlas, UV).rgb * renderer_tint(COLOR.rgb) * tint.rgb;
    ROUGHNESS = 1.0;
}
