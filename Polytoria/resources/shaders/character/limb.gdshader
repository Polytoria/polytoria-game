shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

instance uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, hint_default_transparent;

void fragment() {
	vec4 sample = texture(albedo_texture, UV);
	ALBEDO = mix(albedo.rgb, sample.rgb, sample.a);
}
