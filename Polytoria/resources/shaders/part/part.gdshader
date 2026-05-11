shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

instance uniform vec4 color : source_color = vec4(1.0);

uniform sampler2D albedo : source_color;
uniform bool use_albedo_texture = true;

uniform sampler2D orm;
uniform bool use_orm_texture = true;

uniform sampler2D normal_tex : hint_normal;
uniform bool use_normal_texture = false;

uniform float emissive_strength : hint_range(0.0, 5.0) = 0.0;

uniform float metallic_specular : hint_range(0.0, 1.0) = 0.2;
uniform float roughness : hint_range(0.0, 1.0) = 0.7;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float studs_per_tile = 2.0;

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
	
	float roughness_tex = 1.0;
	float metallic_tex = 1.0;

	if (use_albedo_texture) {
		albedo_color *= texture(albedo, uv);
	}

	if (use_orm_texture) {
		vec3 orm_values = texture(orm, uv).rgb;
		roughness_tex = orm_values.g;
		metallic_tex = orm_values.b;
	}

	if (use_normal_texture) {
		NORMAL_MAP = texture(normal_tex, uv).rgb;
	}

	ALBEDO = albedo_color.rgb * COLOR.rgb;
	SPECULAR = metallic_specular;
	ROUGHNESS = roughness_tex * roughness;
	METALLIC = metallic_tex * metallic;

	if (emissive_strength > 0.0) {
		EMISSION = ALBEDO * emissive_strength;
	}
}

//void light() {
//	// Called for every pixel for every light affecting the material.
//	// Uncomment to replace the default light processing function with this one.
//}
