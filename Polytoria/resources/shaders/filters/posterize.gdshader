shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform int levels = 10;

vec3 posterize(vec3 color, float f_levels) {
	float greyscale = max(color.r, max(color.g, color.b));

	float lower_level = floor(greyscale * f_levels) / f_levels;
	float lower_diff = abs(greyscale - lower_level);

	float upper_level = ceil(greyscale * f_levels) / f_levels;
	float upper_diff = abs(upper_level - greyscale);

	float level = lower_diff <= upper_diff ? lower_level : upper_level;
	float adjustment = level / greyscale;
	return color.rgb * adjustment;
}

void fragment() {
	vec4 color = texture(screen_texture, SCREEN_UV);
	COLOR = vec4(posterize(color.rgb, float(levels)), color.a);
}
