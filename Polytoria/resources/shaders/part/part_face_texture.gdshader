shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D face_texture_0 : source_color, filter_linear_mipmap;
uniform vec4 face_adjust_0 = vec4(0.0, 1.0, 1.0, 0.0);
uniform vec2 face_state_0 = vec2(0.0);

uniform sampler2D face_texture_1 : source_color, filter_linear_mipmap;
uniform vec4 face_adjust_1 = vec4(0.0, 1.0, 1.0, 0.0);
uniform vec2 face_state_1 = vec2(0.0);

uniform sampler2D face_texture_2 : source_color, filter_linear_mipmap;
uniform vec4 face_adjust_2 = vec4(0.0, 1.0, 1.0, 0.0);
uniform vec2 face_state_2 = vec2(0.0);

uniform sampler2D face_texture_3 : source_color, filter_linear_mipmap;
uniform vec4 face_adjust_3 = vec4(0.0, 1.0, 1.0, 0.0);
uniform vec2 face_state_3 = vec2(0.0);

uniform sampler2D face_texture_4 : source_color, filter_linear_mipmap;
uniform vec4 face_adjust_4 = vec4(0.0, 1.0, 1.0, 0.0);
uniform vec2 face_state_4 = vec2(0.0);

uniform sampler2D face_texture_5 : source_color, filter_linear_mipmap;
uniform vec4 face_adjust_5 = vec4(0.0, 1.0, 1.0, 0.0);
uniform vec2 face_state_5 = vec2(0.0);

varying vec3 v_local_pos;
varying vec3 v_local_normal;

int face_index(vec3 n) {
	vec3 an = abs(n);
	if (an.x >= an.y && an.x >= an.z) {
		return n.x > 0.0 ? 3 : 2;
	} else if (an.y >= an.z) {
		return n.y > 0.0 ? 0 : 1;
	}
	return n.z < 0.0 ? 4 : 5;
}

vec2 face_uv(int face, vec3 pos) {
	if (face == 0) {
		return vec2(pos.x, pos.z) + 0.5;
	}
	if (face == 1) {
		return vec2(pos.x, -pos.z) + 0.5;
	}
	if (face == 2) {
		return vec2(pos.z, -pos.y) + 0.5;
	}
	if (face == 3) {
		return vec2(-pos.z, -pos.y) + 0.5;
	}
	if (face == 4) {
		return vec2(-pos.x, -pos.y) + 0.5;
	}
	return vec2(pos.x, -pos.y) + 0.5;
}

void vertex() {
	v_local_pos = VERTEX;
	v_local_normal = normalize(NORMAL);
}

void fragment() {
	vec3 n = normalize(v_local_normal);
	int face = face_index(n);
	vec2 uv = face_uv(face, v_local_pos);

	vec4 tex;
	vec4 adjust;
	vec2 state;

	if (face == 0) {
		tex = texture(face_texture_0, uv);
		adjust = face_adjust_0;
		state = face_state_0;
	} else if (face == 1) {
		tex = texture(face_texture_1, uv);
		adjust = face_adjust_1;
		state = face_state_1;
	} else if (face == 2) {
		tex = texture(face_texture_2, uv);
		adjust = face_adjust_2;
		state = face_state_2;
	} else if (face == 3) {
		tex = texture(face_texture_3, uv);
		adjust = face_adjust_3;
		state = face_state_3;
	} else if (face == 4) {
		tex = texture(face_texture_4, uv);
		adjust = face_adjust_4;
		state = face_state_4;
	} else {
		tex = texture(face_texture_5, uv);
		adjust = face_adjust_5;
		state = face_state_5;
	}

	if (state.x < 0.5) {
		discard;
	}

	vec3 rgb = tex.rgb * exp2(adjust.x);
	rgb = (rgb - 0.5) * adjust.y + 0.5;
	rgb = mix(vec3(dot(rgb, vec3(0.299, 0.587, 0.114))), rgb, adjust.z);
	rgb *= vec3(1.0 + 0.25 * adjust.w, 1.0, 1.0 - 0.25 * adjust.w);

	ALBEDO = clamp(rgb, vec3(0.0), vec3(1.0));
	ALPHA = tex.a * (1.0 - clamp(state.y, 0.0, 1.0));
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
}
