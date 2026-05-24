shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform int pixel_size: hint_range(0, 64) = 0;

void fragment() {
	if (pixel_size > 0) {
		vec2 pixel_coord = round(FRAGCOORD.xy / float(pixel_size)) * float(pixel_size);
		vec2 pixel_uv = pixel_coord * SCREEN_PIXEL_SIZE;
		COLOR = texture(screen_texture, pixel_uv);
	}
}
