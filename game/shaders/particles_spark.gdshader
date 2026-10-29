shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never;
uniform float emission_strength = 2.5;
void fragment() { ALBEDO = COLOR.rgb; EMISSION = COLOR.rgb * emission_strength; ALPHA = COLOR.a; }
