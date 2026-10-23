shader_type canvas_item;

uniform float inner_radius = 0.0;
uniform float outer_radius = 1.0;
uniform vec2 offset = vec2(0.5, 0.5);
uniform vec4 color = vec4(0.0, 0.0, 0.0, 1.0);

void fragment() {
	float radius_dist = max(outer_radius - inner_radius, 0.001);
	float uv_dist = distance(UV, offset) * 2.0;
	float t = clamp(uv_dist - inner_radius, 0.0, radius_dist) / radius_dist;
	vec4 tex_color = texture(TEXTURE, UV);
	COLOR = mix(tex_color, vec4(color.rgb, tex_color.a), t);
}
