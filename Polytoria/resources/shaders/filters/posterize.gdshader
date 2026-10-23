shader_type canvas_item;

uniform ivec3 levels = ivec3(0, 0, 10);

vec3 rgb_to_hsv(vec3 rgb) {
	vec3 hsv = vec3(0.0, 0.0, max(rgb.r, max(rgb.g, rgb.b)));
	float min_color = min(rgb.r, min(rgb.g, rgb.b));
	float delta = hsv.z - min_color;

	hsv.y = hsv.z == 0.0 ? 0.0 : delta / hsv.z;

	if (delta != 0.0) {
		if (hsv.z == rgb.r) {
			hsv.x = mod((rgb.g - rgb.b) / delta, 6.0);
		} else if (hsv.z == rgb.g) {
			hsv.x = (rgb.b - rgb.r) / delta + 2.0;
		} else {
			hsv.x = (rgb.r - rgb.g) / delta + 4.0;
		}
		hsv.x /= 6.0;
	}

	return hsv;
}

vec3 hsv_to_rgb(vec3 hsv) {
	float color = hsv.z * hsv.y;
	float hue = hsv.x * 6.0;
	float x = color * (1.0 - abs(mod(hue, 2.0) - 1.0));
	float m = hsv.z - color;
	vec3 rgb;

	if (hue < 1.0) {
		rgb = vec3(color, x, 0.0);
	} else if (hue < 2.0) {
		rgb = vec3(x, color, 0.0);
	} else if (hue < 3.0) {
		rgb = vec3(0.0, color, x);
	} else if (hue < 4.0) {
		rgb = vec3(0.0, x, color);
	} else if (hue < 5.0) {
		rgb = vec3(x, 0.0, color);
	} else {
		rgb = vec3(color, 0.0, x);
	}

	return rgb + m;
}

float posterize(float value, float f_levels) {
	return floor(value * f_levels) / max(f_levels, 1.0);
}

void fragment() {
	vec4 color = texture(TEXTURE, UV);
	vec3 hsv_color = rgb_to_hsv(color.rgb);

	if (levels.x > 0) {
		hsv_color.x = posterize(hsv_color.x * 0.99, float(levels.x));
	}
	if (levels.y > 0) {
		hsv_color.y = posterize(hsv_color.y * 0.99, float(levels.y));
	}
	if (levels.z > 0) {
		hsv_color.z = posterize(hsv_color.z * 0.99, float(levels.z));
	}

	COLOR = vec4(hsv_to_rgb(hsv_color), color.a);
}
