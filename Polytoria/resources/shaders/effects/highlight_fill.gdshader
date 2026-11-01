shader_type spatial;
render_mode unshaded, blend_mix, cull_back;
stencil_mode read, write, compare_not_equal, 1;

uniform vec4 fill_color : source_color = vec4(1.0, 1.0, 1.0, 0.5);

void fragment() {
	if (!FRONT_FACING) {
		discard;
	}
	if (fill_color.a > 0.001) {
		ALBEDO = fill_color.rgb;
		ALPHA = fill_color.a;
	} else {
		ALBEDO = vec3(0.0);
		ALPHA = 0.0;
	}
}
