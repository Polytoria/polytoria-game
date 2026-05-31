shader_type canvas_item;

uniform int pixel_size: hint_range(0, 64) = 0;

void fragment() {
	if (pixel_size > 0) {
		vec2 pixel_coord = round(FRAGCOORD.xy / float(pixel_size)) * float(pixel_size);
		vec2 pixel_uv = pixel_coord * SCREEN_PIXEL_SIZE;
		COLOR = texture(TEXTURE, pixel_uv);
		COLOR.a = round(COLOR.a);
	}
}
