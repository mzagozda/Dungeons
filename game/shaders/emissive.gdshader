shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D albedo_tex : source_color, filter_nearest, repeat_disable;
uniform float emission_strength = 2.5;
void fragment() {
    ALBEDO = texture(albedo_tex, UV).rgb * COLOR.rgb;
    EMISSION = ALBEDO * emission_strength;
    ROUGHNESS = 1.0;
}
