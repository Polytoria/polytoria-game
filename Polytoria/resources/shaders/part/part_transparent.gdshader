shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 color : source_color = vec4(1.0);

uniform sampler2D albedo : source_color;
uniform bool use_albedo_texture = true;

uniform float emissive_strength : hint_range(0.0, 5.0) = 0.0;

uniform float studs_per_tile = 2.0;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.7;
uniform float specular_value : hint_range(0.0, 1.0) = 0.5;

varying vec3 v_local_pos;
varying vec3 v_local_normal;
varying vec3 v_object_scale;

varying vec4 instance_custom;

vec2 calculate_uv(vec3 pos, vec3 normal)
{
	vec3 an = abs(normal);
	vec2 uv;

	if (an.x >= an.y && an.x >= an.z) {
		uv = pos.zy;
		if (normal.x < 0.0) {
			uv.x = -uv.x;
		}
	} else if (an.y >= an.z) {
		uv = pos.xz;
		if (normal.y < 0.0) {
			uv.y = -uv.y;
		}
	} else {
		uv = pos.xy;
		if (normal.z > 0.0) {
			uv.x = -uv.x;
		}
	}

	return uv;
}

void vertex() {
	v_local_pos = VERTEX;
	v_local_normal = normalize(NORMAL);
	v_object_scale = vec3(
		length(MODEL_MATRIX[0].xyz),
		length(MODEL_MATRIX[1].xyz),
		length(MODEL_MATRIX[2].xyz)
	);
	instance_custom = INSTANCE_CUSTOM;
}

void fragment() {
	vec3 n = normalize(v_local_normal);
	vec3 part_space = v_local_pos * v_object_scale;
	vec2 uv = calculate_uv(part_space, n) / studs_per_tile;
	vec4 albedo_color = color;
	
	if (use_albedo_texture) {
		albedo_color *= texture(albedo, uv);
	}

	ALBEDO = albedo_color.rgb * COLOR.rgb;
	ROUGHNESS = roughness_value;
	SPECULAR = specular_value;
	ALPHA = albedo_color.a * COLOR.a;

	if (emissive_strength > 0.0) {
		EMISSION = ALBEDO * emissive_strength;
	}
}

//void light() {
//	// Called for every pixel for every light affecting the material.
//	// Uncomment to replace the default light processing function with this one.
//}
