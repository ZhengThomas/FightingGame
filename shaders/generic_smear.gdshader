shader_type spatial;
// Flat, unlit, single-color version of slash.gdshader for the light moves: no texture, just a
// color you pick in the material. Shares the same uniform names (opacity/ortho_amount/plane_distance)
// as slash.gdshader, so PlayerVfx and the hitbox-lab preview drive it with no code changes.
//
// unshaded  -> ignores scene lighting, so the color shows flat/pure.
// cull_disabled -> two-sided, visible from both faces.
// blend_mix -> normal alpha blending.
// (Depth testing is left ON so the smear sorts against the characters like normal geometry; PlayerVfx
//  feeds depth_bias to match the attacker's body bias, so it lands in the same draw index.)
render_mode unshaded, cull_disabled, blend_mix;

// The flat color of the effect. Pick it in the material (RGB shows, its alpha caps the max opacity).
uniform vec4 flat_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

// Driven by code: 1 = fully visible, 0 = fully gone. Fades in/out like the textured slash.
uniform float opacity : hint_range(0.0, 1.0) = 1.0;

// Hybrid ortho/perspective projection, matching slash.gdshader / hybrid_projection.gdshader.
// PlayerVfx keeps plane_distance synced to the camera distance (0 = full perspective, 1 = orthographic).
uniform float ortho_amount : hint_range(0.0, 1.0) = 0.7;
uniform float plane_distance = 4.0;

// Sorting-only depth nudge (reverse-Z: + = nearer), matching hybrid_projection.gdshader. PlayerVfx
// sets this to the owner's front/back bias so the smear sorts together with the attacker's body.
uniform float depth_bias = 0.0;

void vertex() {
	vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec4 clip = PROJECTION_MATRIX * view_pos;

	float ortho_ndc_x = view_pos.x * (PROJECTION_MATRIX[0][0] / plane_distance);
	float ortho_ndc_y = view_pos.y * (PROJECTION_MATRIX[1][1] / plane_distance);

	clip.x = mix(clip.x, ortho_ndc_x * clip.w, ortho_amount);
	clip.y = mix(clip.y, ortho_ndc_y * clip.w, ortho_amount);
	clip.z += depth_bias * clip.w; // only affects sorting, not screen position/size
	POSITION = clip;
}

void fragment() {
	ALBEDO = flat_color.rgb;
	ALPHA = flat_color.a * opacity;
}
