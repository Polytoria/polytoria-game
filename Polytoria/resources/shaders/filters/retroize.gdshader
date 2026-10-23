shader_type canvas_item;

uniform int pixel_size: hint_range(0, 64) = 0;
uniform int dither_strength: hint_range(1, 8) = 2;
uniform ivec4 color_depth = ivec4(4, 4, 4, 8);

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

float get_color_step(int depth) {
	if (depth == 0) return 0.0;
	if (depth > 8) return 256.0;
	return pow(2.0, float(depth)) - 1.0;
}

vec2 get_dither_minmax(float color_value, float color_step, float disabled_value) {
	if (color_step <= 0.0) return vec2(disabled_value);
	if (color_step > 256.0) return vec2(color_value);
	return vec2(
		floor(color_value * color_step) / color_step,
		ceil(color_value * color_step) / color_step
	);
}

float step_color_value(float color_value, float color_step, float disabled_value) {
	if (color_step <= 0.0) return disabled_value;
	if (color_step > 256.0) return color_value;
	return round(color_value * color_step) / color_step;
}

void fragment() {
	vec2 pixel_uv = UV;
	ivec2 pixel_idx = ivec2(pixel_uv / SCREEN_PIXEL_SIZE.xy);
	if (pixel_size > 0) {
		vec2 exact_pixel_size = SCREEN_PIXEL_SIZE.xy * float(pixel_size);
		pixel_idx = ivec2(pixel_uv / exact_pixel_size);
		pixel_uv = round(pixel_uv / exact_pixel_size) * exact_pixel_size;
	}

	vec4 color = texture(TEXTURE, pixel_uv);
	vec4 color_step = vec4(
		get_color_step(color_depth.r),
		get_color_step(color_depth.g),
		get_color_step(color_depth.b),
		get_color_step(color_depth.a)
	);

	if (dither_strength > 0) {
		float bayer_value = get_bayer_value(pixel_idx.x, pixel_idx.y, dither_strength);

		float brightness_value = max(max(color.r, color.g), color.b);
		vec2 r = get_dither_minmax(color.r, color_step.r, 0.0);
		vec2 g = get_dither_minmax(color.g, color_step.g, 0.0);
		vec2 b = get_dither_minmax(color.b, color_step.b, 0.0);
		vec2 a = get_dither_minmax(color.a, color_step.a, 1.0);
		vec4 lower_color = vec4(r.x, g.x, b.x, a.x);
		vec4 upper_color = vec4(r.y, g.y, b.y, a.y);

		color = brightness_value >= bayer_value ? upper_color : lower_color;
	} else {
		color = vec4(
			step_color_value(color.r, color_step.r, 0.0),
			step_color_value(color.g, color_step.g, 0.0),
			step_color_value(color.b, color_step.b, 0.0),
			step_color_value(color.a, color_step.a, 1.0)
		);
	}

	COLOR = color;
}
