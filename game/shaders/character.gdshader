shader_type spatial;
render_mode diffuse_burley, specular_disabled;
uniform sampler2D albedo_tex : source_color, filter_nearest, repeat_disable;
uniform float flash : hint_range(0.0, 1.0) = 0.0;
uniform float dissolve : hint_range(0.0, 1.0) = 0.0;
void vertex() { VERTEX.y -= dissolve * 0.5; }
void fragment() {
    ALBEDO = mix(texture(albedo_tex, UV).rgb * COLOR.rgb, vec3(1.0), flash);
    ROUGHNESS = 1.0;
}
