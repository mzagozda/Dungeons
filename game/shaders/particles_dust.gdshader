shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
void fragment() { ALBEDO = COLOR.rgb; ALPHA = COLOR.a; }
