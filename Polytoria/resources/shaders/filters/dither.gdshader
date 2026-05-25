shader_type canvas_item;

const mat3 YIQ_CONVERT = mat3(
	vec3(0.299,  0.596,  0.211),
	vec3(0.587, -0.274, -0.523),
	vec3(0.114, -0.322,  0.312)
);
const mat3 RGB_CONVERT = mat3(
	vec3(1.0,    1.0,    1.0),
	vec3(0.956, -0.272, -1.106),
	vec3(0.621, -0.647,  1.703)
);

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
		return BAYER_MATRIX_1D[y % 2][x % 2] / 4.0;
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

vec3 rgb_to_yiq(vec3 color)
{
	return YIQ_CONVERT * color;
}

vec3 yiq_to_rgb(vec3 color)
{
	return RGB_CONVERT * color;
}

void fragment() {
	vec2 pixel_uv = SCREEN_UV;
	ivec2 pixel_idx = ivec2(pixel_uv / SCREEN_PIXEL_SIZE.xy);
	if (pixel_size > 0) {
		vec2 exact_pixel_size = SCREEN_PIXEL_SIZE.xy * float(pixel_size);
		pixel_idx = ivec2(pixel_uv / exact_pixel_size);
		pixel_uv = round(pixel_uv / exact_pixel_size) * exact_pixel_size;
	}

	vec4 color = texture(screen_texture, pixel_uv);

	if (bayer_resolution > 0) {
		vec3 yiq_color = rgb_to_yiq(color.rgb);
		float bayer_value = get_bayer_value(pixel_idx.x, pixel_idx.y, bayer_resolution);

		float f_brightness_levels = float(brightness_levels);
		float lower_level = floor(yiq_color.x * f_brightness_levels) / f_brightness_levels;
		float upper_level = ceil(yiq_color.x * f_brightness_levels) / f_brightness_levels;

		yiq_color.x = yiq_color.x >= bayer_value ? upper_level : lower_level;
		color.rgb = yiq_to_rgb(yiq_color);
	}

	COLOR = color;
}
