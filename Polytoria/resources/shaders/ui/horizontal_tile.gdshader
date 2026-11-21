shader_type canvas_item;

void fragment() {
	float half_texel = TEXTURE_PIXEL_SIZE.y * 0.5;
	COLOR = texture(TEXTURE, vec2(UV.x, clamp(UV.y, half_texel, 1.0 - half_texel)));
}
