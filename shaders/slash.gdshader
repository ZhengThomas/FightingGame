shader_type spatial;
// unshaded  -> ignores scene lighting (your Emission node), so the texture shows flat/pure.
// cull_disabled -> two-sided, so the flat slash is visible from both faces.
// blend_mix -> normal alpha blending.
// (Depth testing is left ON so the slash sorts against the characters like normal geometry: parts
//  behind the attacker's body are occluded. PlayerVfx feeds depth_bias to match the attacker's own
//  body bias, so the slash lands in the same draw index as the character throwing it.)
render_mode unshaded, cull_disabled, blend_mix;

// Your "Slash Texture" image node. Assign the slash texture here in the material.
uniform sampler2D slash_texture : source_color;

// Your animated "opacity" attribute, now driven by code: 1 = fully visible, 0 = fully gone.
uniform float opacity : hint_range(0.0, 1.0) = 1.0;

// Hybrid ortho/perspective projection, matching hybrid_projection.gdshader so the slash isn't
// perspective-distorted while the characters are flattened. PlayerVfx keeps plane_distance synced
// to the camera distance (0 = full perspective, 1 = fully orthographic).
uniform float ortho_amount : hint_range(0.0, 1.0) = 0.7;
uniform float plane_distance = 4.0;

// Sorting-only depth nudge (reverse-Z: + = nearer), matching hybrid_projection.gdshader. PlayerVfx
// sets this to the owner's front/back bias so the slash sorts together with the attacker's body.
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
	vec4 tex = texture(slash_texture, UV);

	// Emission node -> just the texture color, unlit.
	ALBEDO = tex.rgb;

	// Your Subtract look: knock the image's baked alpha down by (1 - opacity), so faint edges
	// erode away first. opacity = 1 -> full texture alpha; opacity = 0 -> fully gone.
	ALPHA = clamp(tex.a - (1.0 - opacity), 0.0, 1.0);
}
