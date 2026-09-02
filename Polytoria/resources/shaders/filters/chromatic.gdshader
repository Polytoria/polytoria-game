shader_type canvas_item;

uniform int levels = 3;
uniform float spread = 0.01;

vec3 chromatic_slice(float t) {
	vec3 res = vec3(1.0 - t, 1.0 - abs(t - 1.0), t - 1.0);
	return max(res, 0.0);
}

void fragment() {
	COLOR.rgb = vec3(0);
	vec3 sum = vec3(0);
	vec2 offset = (UV - vec2(0.5)) * vec2(1, -1);
	for (int i = 0; i < levels; i++) {
		float t = 2.0 * float(i) / float(levels - 1);
		vec3 slice = chromatic_slice(t);
		sum += slice;
		vec2 slice_offset = (t - 1.0) * spread * offset;
		COLOR.rgb += slice * texture(TEXTURE, UV + slice_offset).rgb;
	}
	COLOR.rgb /= sum;
}
