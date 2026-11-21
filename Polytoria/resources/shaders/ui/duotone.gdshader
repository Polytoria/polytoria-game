shader_type canvas_item;

uniform vec4 shadow_color : source_color = vec4(0.25, 0.04, 0.06, 1.0);
uniform vec4 highlight_color : source_color = vec4(1.0, 0.62, 0.6, 1.0);
uniform float contrast = 1.0;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	float luma = dot(tex.rgb, vec3(0.299, 0.587, 0.114));
	luma = clamp((luma - 0.5) * contrast + 0.5, 0.0, 1.0);
	COLOR = vec4(mix(shadow_color.rgb, highlight_color.rgb, luma), tex.a * COLOR.a);
}
