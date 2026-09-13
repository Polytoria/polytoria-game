shader_type sky;

// Sun Disc
uniform vec3 sun_disc_color : source_color = vec3(1.0, 1.0, 1.0);
uniform float sun_disc_size : hint_range(0.005, 0.5) = 0.04;
uniform sampler2D sun_image : source_color, filter_linear;
uniform bool sun_has_image = false;

// Sun Halo
uniform vec3 sun_halo_color : source_color = vec3(0.8970588, 0.7760561, 0.6661981);
uniform float sun_halo_exponent : hint_range(50.0, 500.0) = 125.0;
uniform float sun_halo_contribution : hint_range(0.0, 1.0) = 0.75;

// Moon Disc
uniform vec3 moon_disc_color : source_color = vec3(0.85, 0.88, 1.0);
uniform float moon_disc_size : hint_range(0.005, 0.5) = 0.02;
uniform sampler2D moon_image : source_color, filter_linear;
uniform bool moon_has_image = false;

// Moon Halo
uniform vec3 moon_halo_color : source_color = vec3(0.6, 0.65, 0.85);
uniform float moon_halo_exponent : hint_range(50.0, 500.0) = 125.0;
uniform float moon_halo_contribution : hint_range(0.0, 1.0) = 0.5;

// Horizon Line
uniform vec3 horizon_line_color : source_color = vec3(0.9044118, 0.8872592, 0.7913603);
uniform float horizon_line_exponent : hint_range(1.0, 10.0) = 4.0;
uniform float horizon_line_contribution : hint_range(0.0, 1.0) = 0.25;

// Sky Gradient
uniform vec3 sky_gradient_top : source_color = vec3(0.172549, 0.5686274, 0.6941177);
uniform vec3 sky_gradient_bottom : source_color = vec3(0.764706, 0.8156863, 0.8509805);
uniform float sky_gradient_exponent : hint_range(0.5, 5.0) = 2.5;

// Stars
uniform bool stars_enabled = true;
uniform float stars_density : hint_range(0.9, 0.999) = 0.98;
uniform float stars_brightness : hint_range(0.0, 5.0) = 1.0;
uniform float stars_twinkle_speed : hint_range(0.0, 5.0) = 1.0;
uniform float stars_horizon_padding : hint_range(0.0, 0.99) = 0.0;
uniform mat3 stars_rotation = mat3(1.0);

// Clouds
uniform bool clouds_enabled = false;
uniform float clouds_coverage : hint_range(0.0, 1.0) = 0.5;
uniform float clouds_softness : hint_range(0.0, 1.0) = 0.5;
uniform float clouds_scale : hint_range(0.1, 3.0) = 1.0;
uniform vec2 clouds_speed = vec2(0.01, 0.0);
uniform vec4 clouds_color : source_color = vec4(1.0, 1.0, 1.0, 0.8);

uniform bool use_cubemap = false;
uniform bool use_nearest_filter = false;

uniform sampler2D front_linear : source_color, filter_linear, repeat_disable;
uniform sampler2D back_linear : source_color, filter_linear, repeat_disable;
uniform sampler2D left_linear : source_color, filter_linear, repeat_disable;
uniform sampler2D right_linear : source_color, filter_linear, repeat_disable;
uniform sampler2D top_linear : source_color, filter_linear, repeat_disable;
uniform sampler2D bottom_linear : source_color, filter_linear, repeat_disable;
uniform sampler2D front_nearest : source_color, filter_nearest, repeat_disable;
uniform sampler2D back_nearest : source_color, filter_nearest, repeat_disable;
uniform sampler2D left_nearest : source_color, filter_nearest, repeat_disable;
uniform sampler2D right_nearest : source_color, filter_nearest, repeat_disable;
uniform sampler2D top_nearest : source_color, filter_nearest, repeat_disable;
uniform sampler2D bottom_nearest : source_color, filter_nearest, repeat_disable;

uniform vec3 fake_body_dir[16];
uniform vec3 fake_body_color[16];
uniform float fake_body_size[16];
uniform int fake_body_count = 0;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Stars
float _sh(vec3 p) {
	p = fract(p * vec3(127.1, 311.7, 74.7));
	p += dot(p, p.zxy + 31.41);
	p = fract(p * vec3(269.5, 183.3, 461.1));
	return fract(dot(p, vec3(1.0, 7.0, 3.0)));
}

vec3 _star_tint(float t) {
	return mix(
		vec3(1.0, 0.75, 0.5),
		mix(vec3(1.0, 0.98, 0.95), vec3(0.75, 0.88, 1.0), t),
		sqrt(t)
	);
}

vec3 _star_cell(vec3 cell, vec3 dir, float thresh, float sz_mult, float br_mult) {
	if (_sh(cell + vec3(71.3, 53.9, 97.1)) > thresh) return vec3(0.0);
	float h1 = _sh(cell + vec3(3.7, 0.0, 0.0));
	float h2 = _sh(cell + vec3(0.0, 17.3, 0.0));
	float h3 = _sh(cell + vec3(0.0, 0.0, 31.7));
	float hb = _sh(cell + vec3(43.1, 19.3, 7.5));
	float hc = _sh(cell + vec3(59.3, 37.1, 23.9));
	vec3 star_dir = normalize(cell + vec3(h1, h2, h3) - 0.5);
	float cos_a = dot(dir, star_dir);
	float r = 0.0015 * sz_mult * (hb * 0.6 + 0.4);
	float cos_edge = 1.0 - r * r;
	if (cos_a < cos_edge) return vec3(0.0);
	float twinkle = stars_twinkle_speed > 0.0 ? (0.7 + 0.3 * sin(TIME * stars_twinkle_speed + hb * 30.0)) : 1.0;
	return _star_tint(hc) * (hb * hb) * br_mult * smoothstep(cos_edge, 1.0, cos_a) * twinkle;
}

vec3 _star_layer(vec3 dir, float scale, float thresh, float sz_mult, float br_mult) {
	vec3 f = dir * scale;
	vec3 nc = floor(f);
	vec3 fc = fract(f);
	vec3 result = _star_cell(nc, dir, thresh, sz_mult, br_mult);
	if (fc.x < 0.25) result = max(result, _star_cell(nc + vec3(-1, 0, 0), dir, thresh, sz_mult, br_mult));
	else if (fc.x > 0.75) result = max(result, _star_cell(nc + vec3(1, 0, 0), dir, thresh, sz_mult, br_mult));
	if (fc.y < 0.25) result = max(result, _star_cell(nc + vec3(0, -1, 0), dir, thresh, sz_mult, br_mult));
	else if (fc.y > 0.75) result = max(result, _star_cell(nc + vec3(0, 1, 0), dir, thresh, sz_mult, br_mult));
	if (fc.z < 0.25) result = max(result, _star_cell(nc + vec3(0, 0, -1), dir, thresh, sz_mult, br_mult));
	else if (fc.z > 0.75) result = max(result, _star_cell(nc + vec3(0, 0, 1), dir, thresh, sz_mult, br_mult));
	return result;
}

// Clouds
float smooth_noise(vec2 p, float seed) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(hash(vec3(i, seed)), hash(vec3(i + vec2(1.0, 0.0), seed)), u.x),
		mix(hash(vec3(i + vec2(0.0, 1.0), seed)), hash(vec3(i + vec2(1.0, 1.0), seed)), u.x),
		u.y
	);
}

float cloud_fbm(vec2 p, float seed) {
	float v = 0.0, amp = 0.5;
	for (int i = 0; i < 5; i++) {
		v += amp * smooth_noise(p, seed + float(i) * 31.7);
		p *= 2.1;
		amp *= 0.5;
	}
	return v;
}

vec3 sample_cubemap_linear(vec3 dir) {
	vec3 absvec = abs(dir);
	if (absvec.x >= absvec.y && absvec.x >= absvec.z)
		return dir.x >= 0.0
			? texture(left_linear, (vec2(-dir.z, -dir.y) / absvec.x) * 0.5 + 0.5).rgb
			: texture(right_linear, (vec2(dir.z, -dir.y) / absvec.x) * 0.5 + 0.5).rgb;
	if (absvec.y >= absvec.x && absvec.y >= absvec.z)
		return dir.y >= 0.0
			? texture(top_linear, (vec2(dir.x, dir.z) / absvec.y) * 0.5 + 0.5).rgb
			: texture(bottom_linear, (vec2(dir.x, -dir.z) / absvec.y) * 0.5 + 0.5).rgb;
	return dir.z >= 0.0
		? texture(front_linear, (vec2(dir.x, -dir.y) / absvec.z) * 0.5 + 0.5).rgb
		: texture(back_linear, (vec2(-dir.x, -dir.y) / absvec.z) * 0.5 + 0.5).rgb;
}

vec3 sample_cubemap_nearest(vec3 dir) {
	vec3 absvec = abs(dir);
	if (absvec.x >= absvec.y && absvec.x >= absvec.z)
		return dir.x >= 0.0
			? texture(left_nearest, (vec2(-dir.z, -dir.y) / absvec.x) * 0.5 + 0.5).rgb
			: texture(right_nearest, (vec2(dir.z, -dir.y) / absvec.x) * 0.5 + 0.5).rgb;
	if (absvec.y >= absvec.x && absvec.y >= absvec.z)
		return dir.y >= 0.0
			? texture(top_nearest, (vec2(dir.x, dir.z) / absvec.y) * 0.5 + 0.5).rgb
			: texture(bottom_nearest, (vec2(dir.x, -dir.z) / absvec.y) * 0.5 + 0.5).rgb;
	return dir.z >= 0.0
		? texture(front_nearest, (vec2(dir.x, -dir.y) / absvec.z) * 0.5 + 0.5).rgb
		: texture(back_nearest, (vec2(-dir.x, -dir.y) / absvec.z) * 0.5 + 0.5).rgb;
}

void sky() {
	vec3 dir = normalize(EYEDIR);

	if (use_cubemap) {
		COLOR = use_nearest_filter ? sample_cubemap_nearest(dir) : sample_cubemap_linear(dir);

	} else {

		// Get the current pixel's direction in world space
		float mask_horizon = dot(dir, vec3(0.0, 1.0, 0.0));
		float night_factor = smoothstep(-0.05, 0.35, -LIGHT0_DIRECTION.y);

		// Sun disc
		float mask_sun = clamp(pow(clamp(dot(dir, LIGHT0_DIRECTION), 0.0, 1.0), 5000.0 / max(sun_disc_size, 0.001)), 0.0, 1.0);
		vec3 sun_up = abs(LIGHT0_DIRECTION.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
		vec3 sun_right = normalize(cross(sun_up, LIGHT0_DIRECTION));
		sun_up = normalize(cross(LIGHT0_DIRECTION, sun_right));
		vec2 sun_uv = vec2(dot(dir, sun_right), dot(dir, sun_up)) * (0.5 / sun_disc_size) + 0.5;
		bool sun_uv_valid = sun_uv.x >= 0.0 && sun_uv.x <= 1.0 && sun_uv.y >= 0.0 && sun_uv.y <= 1.0;
		vec4 sun_tex_sample = (sun_has_image && sun_uv_valid) ? texture(sun_image, sun_uv) : vec4(0.0);
		vec3 sun_tex_col = mix(sun_disc_color, sun_tex_sample.rgb * sun_disc_color, sun_tex_sample.a * float(sun_has_image && sun_uv_valid));

		// Sun halo
		vec3 sun_halo_col = sun_halo_color * sun_halo_contribution;
		float sun_bell = pow(clamp(dot(dir, LIGHT0_DIRECTION), 0.0, 1.0), sun_halo_exponent * clamp(abs(mask_horizon), 0.0, 1.0));
		float horizon_soften = 1.0 - pow(1.0 - clamp(mask_horizon, 0.0, 1.0), 50.0);
		sun_halo_col *= clamp(sun_bell * horizon_soften, 0.0, 1.0);

		// Moon disc
		float mask_moon = clamp(pow(clamp(dot(dir, LIGHT1_DIRECTION), 0.0, 1.0), 5000.0 / max(moon_disc_size, 0.001)), 0.0, 1.0);
		vec3 moon_up = abs(LIGHT1_DIRECTION.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
		vec3 moon_right = normalize(cross(moon_up, LIGHT1_DIRECTION));
		moon_up = normalize(cross(LIGHT1_DIRECTION, moon_right));
		vec2 moon_uv = vec2(dot(dir, moon_right), dot(dir, moon_up)) * (0.5 / moon_disc_size) + 0.5;
		bool moon_uv_valid = moon_uv.x >= 0.0 && moon_uv.x <= 1.0 && moon_uv.y >= 0.0 && moon_uv.y <= 1.0;
		vec4 moon_tex_sample = (moon_has_image && moon_uv_valid) ? texture(moon_image, moon_uv) : vec4(0.0);
		vec3 moon_tex_col = mix(moon_disc_color, moon_tex_sample.rgb * moon_disc_color, moon_tex_sample.a * float(moon_has_image && moon_uv_valid));

		// Moon halo
		vec3 moon_halo_col = moon_halo_color * moon_halo_contribution;
		float moon_bell = pow(clamp(dot(dir, LIGHT1_DIRECTION), 0.0, 1.0), moon_halo_exponent * clamp(abs(mask_horizon), 0.0, 1.0));
		moon_halo_col *= clamp(moon_bell * horizon_soften, 0.0, 1.0);

		// Horizon line
		vec3 horizon_line_col = horizon_line_color * clamp(pow(1.0 - abs(mask_horizon), horizon_line_exponent), 0.0, 1.0);
		horizon_line_col = mix(vec3(0.0), horizon_line_col, horizon_line_contribution);

		// Sky gradient
		vec3 sky_gradient_col = mix(sky_gradient_top, sky_gradient_bottom, pow(1.0 - clamp(mask_horizon, 0.0, 1.0), max(sky_gradient_exponent, 0.01)));

		// Dim background
		vec3 base_color = clamp(sun_halo_col + moon_halo_col + horizon_line_col + sky_gradient_col, 0.0, 1.0);
		base_color = mix(base_color, vec3(0.0), night_factor * 0.95);

		// Composite discs
		base_color = mix(base_color, moon_tex_col, mask_moon * (1.0 - mask_sun));
		vec3 final_color = mix(base_color, sun_tex_col, mask_sun);

		// Stars
		if (stars_enabled && mask_horizon > stars_horizon_padding) {
			float edge_fade = smoothstep(stars_horizon_padding, stars_horizon_padding + 0.1, mask_horizon);
			float thresh = 1.0 - stars_density;
			vec3 rd = normalize(stars_rotation * dir);
			vec3 star_col = _star_layer(rd, 80.0,  thresh * 3.0, 2.0, 1.0) + _star_layer(rd, 350.0, thresh, 0.8, 0.4);
			final_color += star_col * stars_brightness * edge_fade * night_factor;
		}

		// Decorative bodies (planets, extra suns; no light data, drawn as flat discs)
		for (int i = 0; i < fake_body_count; i++) {
			float m = clamp(pow(clamp(dot(dir, fake_body_dir[i]), 0.0, 1.0), fake_body_size[i] * 100000.0), 0.0, 1.0);
			final_color = mix(final_color, fake_body_color[i], m);
		}

		// Clouds
		if (clouds_enabled && mask_horizon > 0.01) {
			vec2 cloud_uv = dir.xz / sqrt(max(dir.y, 0.01));
			float spd = length(clouds_speed);
			vec2 cloud_dir = spd > 0.001 ? normalize(clouds_speed) : vec2(1.0, 0.0);
			float cutoff = 1.0 - clouds_coverage;
			float fuzz = max(clouds_softness, 0.01);
			float n0 = cloud_fbm((cloud_uv + cloud_dir * (TIME * spd)) * clouds_scale, 0.0);
			float n1 = cloud_fbm((cloud_uv + cloud_dir * (TIME * spd * 0.89)) * clouds_scale, 17.3);
			float n2 = cloud_fbm((cloud_uv + cloud_dir * (TIME * spd * 0.79)) * clouds_scale, 53.1);
			n2 = smoothstep(cutoff, cutoff + fuzz, n2);
			n1 = smoothstep(cutoff, cutoff + fuzz, n1 + n2 * 0.2) * 1.1;
			n0 = smoothstep(cutoff, cutoff + fuzz, n0 + n1 * 0.4) * 1.2;
			float cloud_amt = clamp(n0 + n1 + n2, 0.0, 1.0) * smoothstep(0.01, 0.08, mask_horizon);
			vec3 cloud_col = mix(min(clouds_color.rgb * 1.15, vec3(1.0)), clouds_color.rgb, clamp(n0, 0.0, 1.0));

			// Warm sun scatter near the disc
			float sun_prox = pow(clamp(dot(dir, LIGHT0_DIRECTION), 0.0, 1.0), 8.0);
			cloud_col = mix(cloud_col, min(sun_disc_color * 1.2, vec3(1.5)), sun_prox * (1.0 - night_factor) * 0.35);

			// Scotopic grading (desaturate + cool tint as light fades)
			float cloud_lum = dot(cloud_col, vec3(0.299, 0.587, 0.114));
			cloud_col = mix(cloud_col, cloud_lum * vec3(0.6, 0.75, 0.95), 0.8 * night_factor);
			final_color = mix(final_color, cloud_col * mix(1.0, 0.12, night_factor), cloud_amt * clouds_color.a);
		}

		COLOR = final_color;
	}
}