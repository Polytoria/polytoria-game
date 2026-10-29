shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_test_disabled;

instance uniform float value = 1.0;

uniform vec2 size = vec2(300.0, 30.0);
uniform float radius = 8.0;
uniform float border = 4.0;
uniform vec4 bg_color : source_color = vec4(0.0, 0.0, 0.0, 0.498);
uniform vec4 bg_border_color : source_color = vec4(0.0, 0.0, 0.0, 0.871);
uniform vec4 fill_color : source_color = vec4(0.309, 0.91, 0.51, 1.0);
uniform vec4 fill_border_color : source_color = vec4(0.09, 0.09, 0.09, 0.553);

vec4 box(vec2 p, float width, vec4 color, vec4 edge_color) {
	vec2 half_size = vec2(width, size.y) * 0.5;
	float r = min(radius, half_size.x);
	vec2 q = abs(p - half_size) - (half_size - vec2(r));
	float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
	float edge = fwidth(dist) * 0.5;
	vec4 c = mix(color, edge_color, smoothstep(-border - edge, -border + edge, dist));
	c.a *= 1.0 - smoothstep(-edge, edge, dist);
	return vec4(c.rgb * c.a, c.a);
}

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(MAIN_CAM_INV_VIEW_MATRIX[0], MAIN_CAM_INV_VIEW_MATRIX[1], MAIN_CAM_INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}

void fragment() {
	vec2 p = UV * size;
	vec4 c = box(p, size.x, bg_color, bg_border_color);
	if (value > 0.0) {
		vec4 fill = box(p, border * 2.0 + min(value, 1.0) * (size.x - border * 2.0), fill_color, fill_border_color);
		c = fill + c * (1.0 - fill.a);
	}
	ALBEDO = c.rgb;
	ALPHA = c.a;
}
