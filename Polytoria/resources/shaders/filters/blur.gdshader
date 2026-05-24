shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float blur_strength: hint_range(0.0, 1.0) = 0.05;

void fragment() {
	COLOR = texture(screen_texture, SCREEN_UV, blur_strength);
}
