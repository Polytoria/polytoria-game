shader_type canvas_item;

uniform float blur_strength: hint_range(0.0, 1.0) = 0.05;

void fragment() {
	COLOR = texture(TEXTURE, UV, blur_strength);
}
