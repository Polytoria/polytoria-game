shader_type sky;

uniform sampler2D front : source_color, filter_linear, repeat_disable;
uniform sampler2D back : source_color, filter_linear, repeat_disable;
uniform sampler2D left : source_color, filter_linear, repeat_disable;
uniform sampler2D right : source_color, filter_linear, repeat_disable;
uniform sampler2D top : source_color, filter_linear, repeat_disable;
uniform sampler2D bottom : source_color, filter_linear, repeat_disable;

void sky() {
	COLOR = EYEDIR;
	vec3 dir = normalize(EYEDIR);
    vec3 absvec = abs(dir);
	
	if (absvec.x >= absvec.y && absvec.x >= absvec.z)
	{
		if (dir.x >= 0.0)
		{
			COLOR = texture(left, (vec2(-dir.z, -dir.y) / absvec.x) * 0.5 + 0.5).rgb;
		} else {
			COLOR = texture(right, (vec2(dir.z, -dir.y) / absvec.x) * 0.5 + 0.5).rgb;
		}
	} else if (absvec.y >= absvec.x && absvec.y >= absvec.z)
	{
		if (dir.y >= 0.0)
		{
			COLOR = texture(top, (vec2(dir.x, dir.z) / absvec.y) * 0.5 + 0.5).rgb;
		} else {
			COLOR = texture(bottom, (vec2(dir.x, -dir.z) / absvec.y) * 0.5 + 0.5).rgb;
		}
	} else
	{
		if (dir.z >= 0.0)
		{
			COLOR = texture(front, (vec2(dir.x, -dir.y) / absvec.z) * 0.5 + 0.5).rgb;
		} else {
			COLOR = texture(back, (vec2(-dir.x, -dir.y) / absvec.z) * 0.5 + 0.5).rgb;
		}
	}
}