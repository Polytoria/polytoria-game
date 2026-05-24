shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform int pixel_size: hint_range(0, 64) = 0;
uniform int bayer_resolution: hint_range(1, 8) = 2;
uniform int brightness_levels: hint_range(1, 16) = 4;

const mat2 BAYER_MATRIX_1D = mat2(
	vec2(0.0, 2.0),
	vec2(3.0, 1.0)
);

float get_bayer_value(int x, int y, int resolution) {
	if (resolution <= 1) {
		return BAYER_MATRIX_1D[y % 2][x % 2];
	}

	float res_float = float(resolution);
	float matrix_size = pow(2.0, res_float * 2.0);
	float row_size = pow(2.0, res_float) / 2.0;
	float value_step = 1.0;
	float value = 0.0;

	do {
		float matrix_value = BAYER_MATRIX_1D[y / int(row_size) % 2][x / int(row_size) % 2];
		value += value_step * matrix_value;
		value_step *= 4.0;
		row_size = floor(row_size / 2.0);
	} while (row_size < 1.0);

	return value / (matrix_size - 1.0);
}

void fragment() {
	vec2 pixel_uv = SCREEN_UV;
	if (pixel_size > 0) {
		vec2 exact_pixel_size = SCREEN_PIXEL_SIZE.xy * float(pixel_size);
		pixel_uv = round(pixel_uv / exact_pixel_size) * exact_pixel_size;
	}

	vec4 color = texture(screen_texture, pixel_uv);
	float greyscale = max(color.r, max(color.g, color.b));
	ivec2 dither_position = ivec2(pixel_uv / SCREEN_PIXEL_SIZE.xy);

	int bayer_row_size = int(pow(2, float(bayer_resolution))) / 2;
	float bayer_value = get_bayer_value(dither_position.x % 4, dither_position.y % 4, bayer_resolution);

	float f_brightness_levels = float(brightness_levels);
	float lower_level = floor(greyscale * f_brightness_levels) / f_brightness_levels;
	float upper_level = ceil(greyscale * f_brightness_levels) / f_brightness_levels;

	float level = greyscale >= bayer_value ? lower_level : upper_level;
	float adjustment = level / greyscale;

	COLOR = vec4(color.rgb * adjustment, color.a);
}
