shader_type spatial;

// Outline shell for the character. Same ortho/perspective vertex blend as the body on both axes
// (they MUST match, or the outline slides off the model). Renders the inside of the shell, flat.
//
// cull_back matches how your outline material was imported. If the outline looks inverted or
// missing, try changing this to cull_front or cull_disabled.
render_mode unshaded, cull_back;

// Keep these two IDENTICAL to the body shader's values.
uniform float ortho_amount : hint_range(0.0, 1.0) = 0.7;
uniform float plane_distance = 4.0;

uniform vec4 outline_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);

// Keep IDENTICAL to the body shader so the outline sorts with its body (see body shader notes).
uniform float depth_bias = 0.0;

void vertex() {
    vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    vec4 clip = PROJECTION_MATRIX * view_pos;

    float ortho_ndc_x = view_pos.x * (PROJECTION_MATRIX[0][0] / plane_distance);
    float ortho_ndc_y = view_pos.y * (PROJECTION_MATRIX[1][1] / plane_distance);

    clip.x = mix(clip.x, ortho_ndc_x * clip.w, ortho_amount);
    clip.y = mix(clip.y, ortho_ndc_y * clip.w, ortho_amount);
    clip.z += depth_bias * clip.w; // reverse-Z: + = nearer. Only affects sorting, not size.
    POSITION = clip;
}

void fragment() {
    ALBEDO = outline_color.rgb;
}
