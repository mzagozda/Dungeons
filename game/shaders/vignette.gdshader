shader_type canvas_item;
render_mode unshaded;
uniform float strength : hint_range(0.0, 1.0) = 0.25;
void fragment() {
    float edge = smoothstep(0.25, 0.7071, length(UV - vec2(0.5)));
    COLOR = vec4(0.0, 0.0, 0.0, strength * edge);
}
