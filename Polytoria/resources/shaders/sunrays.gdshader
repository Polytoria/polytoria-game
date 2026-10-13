shader_type canvas_item;
render_mode unshaded, blend_add;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform vec2 light_pos_screen = vec2(0.5);
uniform vec3 light_dir = vec3(0.0, -1.0, 0.0);
uniform vec3 camera_dir = vec3(0.0, 0.0, -1.0);
uniform float fov_compensation = 1.0;
uniform float sun_occlusion : hint_range(0.0, 1.0) = 1.0;

uniform bool enabled = true;
uniform float intensity : hint_range(0.0, 2.0) = 0.5;
uniform float ray_length : hint_range(0.0, 1.0) = 0.5;
uniform vec3 tint : source_color = vec3(1.0, 0.95, 0.8);
uniform int sample_count = 96;

float _hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

vec3 lensflare(vec2 uv, vec2 pos) {
	vec2 uvd = uv * length(uv);

	float f2  = max(1.0 / (1.0 + 32.0 * pow(length(uvd + 0.8   * pos), 2.0)), 0.0) * 0.25;
	float f22 = max(1.0 / (1.0 + 32.0 * pow(length(uvd + 0.85  * pos), 2.0)), 0.0) * 0.23;
	float f23 = max(1.0 / (1.0 + 32.0 * pow(length(uvd + 0.9   * pos), 2.0)), 0.0) * 0.21;

	vec2 uvx = mix(uv, uvd, -0.5);

	float f4  = max(0.01 - pow(length(uvx + 0.4  * pos), 2.4), 0.0) * 6.0;
	float f42 = max(0.01 - pow(length(uvx + 0.45 * pos), 2.4), 0.0) * 5.0;
	float f43 = max(0.01 - pow(length(uvx + 0.5  * pos), 2.4), 0.0) * 3.0;

	uvx = mix(uv, uvd, -0.4);

	float f5  = max(0.01 - pow(length(uvx + 0.2  * pos), 5.5), 0.0) * 2.0;
	float f52 = max(0.01 - pow(length(uvx + 0.4  * pos), 5.5), 0.0) * 2.0;
	float f53 = max(0.01 - pow(length(uvx + 0.6  * pos), 5.5), 0.0) * 2.0;

	uvx = mix(uv, uvd, -0.5);

	float f6  = max(0.01 - pow(length(uvx - 0.3   * pos), 1.6), 0.0) * 6.0;
	float f62 = max(0.01 - pow(length(uvx - 0.325 * pos), 1.6), 0.0) * 3.0;
	float f63 = max(0.01 - pow(length(uvx - 0.35  * pos), 1.6), 0.0) * 5.0;

	vec3 c = vec3(f2 + f4 + f5 + f6, f22 + f42 + f52 + f62, f23 + f43 + f53 + f63);

	return max(c * 1.3 - vec3(length(uvd) * 0.05), vec3(0.0));
}

void fragment() {
	float facing = clamp(-dot(normalize(camera_dir), normalize(light_dir)), 0.0, 1.0);
	vec3 result = vec3(0.0);

	if (enabled) {

		// Rays
		float effective_length = clamp(ray_length * fov_compensation, 0.0, 1.0);
		vec2 delta = SCREEN_UV - light_pos_screen;
		float light_val = 0.0;
		int count = clamp(sample_count, 1, 200);
		for (int i = 0; i < 200; i++) {
			if (i >= count) break;
			float scale = 1.0 - effective_length * (float(i) / float(count - 1));
			vec2 sample_uv = clamp(delta * scale + light_pos_screen, vec2(0.0), vec2(1.0));
			float lum = dot(texture(screen_texture, sample_uv).rgb, vec3(0.299, 0.587, 0.114));

			// Only sun-bright pixels contribute
			float sun_only = pow(max(lum - 0.55, 0.0) / 0.45, 2.0);
			light_val += sun_only / float(count);
		}
		light_val *= 1.0 - _hash(SCREEN_UV) * 0.2;
		result += tint * light_val * intensity;

		// Flare
		vec2 res = 1.0 / SCREEN_PIXEL_SIZE;
		vec2 uv = FRAGCOORD.xy / res - 0.5;
		uv.x *= res.x / res.y;

		vec2 pos = light_pos_screen - vec2(0.5);
		pos.x *= res.x / res.y;

		result += lensflare(uv, pos) * tint * intensity * 5.0 * sun_occlusion * (1.0 - _hash(FRAGCOORD.xy) * 0.015);
	}

	COLOR = vec4(result * facing, 1.0);
}