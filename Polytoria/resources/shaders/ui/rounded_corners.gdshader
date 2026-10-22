shader_type canvas_item;

instance uniform vec2 size = vec2(114.0, 114.0);
uniform float radius = 10.0;

varying vec2 local_position;

void vertex() {
	local_position = VERTEX;
}

void fragment() {
	vec2 half_size = size * 0.5;
	vec2 q = abs(local_position - half_size) - (half_size - vec2(radius));
	float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
	float edge = fwidth(dist);
	COLOR.a *= 1.0 - smoothstep(-edge, edge, dist);
}
