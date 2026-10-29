shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
void fragment() {
    ALBEDO = vec3(0.025, 0.018, 0.035);
    ALPHA = (1.0 - smoothstep(0.25, 0.5, length(UV - vec2(0.5)))) * 0.35;
}
