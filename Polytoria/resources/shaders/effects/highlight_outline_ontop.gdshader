shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_test_disabled;
stencil_mode read, compare_not_equal, 2;

uniform vec4 outline_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float outline_size = 0.0;
uniform bool camera_inside = false;

void vertex() {
	if (outline_size > 0.0) {
		vec3 world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
		vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		world_pos += world_normal * outline_size;
		VERTEX = (inverse(MODEL_MATRIX) * vec4(world_pos, 1.0)).xyz;
	}
}

void fragment() {
	if (camera_inside || !FRONT_FACING) {
		discard;
	}
	ALBEDO = outline_color.rgb;
	ALPHA = outline_color.a;
}
