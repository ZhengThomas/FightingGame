shader_type spatial;
render_mode unshaded;

uniform float grid_size : hint_range(0.1, 10.0) = 1.0;
uniform vec4 line_color : source_color = vec4(0.08, 0.08, 0.08, 1.0);
uniform vec4 fill_color : source_color = vec4(0.7, 0.7, 0.7, 1.0);
uniform float line_width : hint_range(0.01, 0.49) = 0.1;
uniform float emission_strength : hint_range(0.0, 10.0) = 1.0;

void fragment() {
	vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 world_normal = abs(normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz));

	vec2 uv;
	if (world_normal.y > world_normal.x && world_normal.y > world_normal.z) {
		// Floor / ceiling — use XZ
		uv = world_pos.xz;
	} else if (world_normal.z > world_normal.x) {
		// Front / back wall — use XY
		uv = world_pos.xy;
	} else {
		// Side wall — use ZY
		uv = world_pos.zy;
	}

	vec2 grid = fract(uv / grid_size);
	float line = step(1.0 - line_width, grid.x) + step(1.0 - line_width, grid.y);
	line = clamp(line, 0.0, 1.0);

	vec3 color = mix(fill_color.rgb, line_color.rgb, line);
	ALBEDO = color;
	EMISSION = line_color.rgb * line * emission_strength;
}