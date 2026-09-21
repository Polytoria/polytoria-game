shader_type canvas_item;

uniform float shift = 0.0;

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

vec3 rgb_to_yiq(vec3 color)
{
	return YIQ_CONVERT * color;
}

vec3 yiq_to_rgb(vec3 color)
{
	return RGB_CONVERT * color;
}

void fragment() {
	vec4 color = texture(TEXTURE, UV);
	vec3 yiq = rgb_to_yiq(color.rgb);
	mat2 rotation_matrix = mat2(
		vec2(cos(shift), -sin(shift)),
		vec2(sin(shift), cos(shift))
	);
	yiq.yz *= rotation_matrix;
	COLOR = vec4(yiq_to_rgb(yiq), color.a);
}
