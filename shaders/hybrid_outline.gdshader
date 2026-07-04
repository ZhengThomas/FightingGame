shader_type spatial;

// Outline shell for the character. Same horizontal ortho/perspective vertex blend as the body
// (they MUST match, or the outline slides off the model). Renders the inside of the shell, flat.
//
// cull_back matches how your outline material was imported. If the outline looks inverted or
// missing, try changing this to cull_front or cull_disabled.
render_mode unshaded, cull_back;

// Keep these two IDENTICAL to the body shader's values.
uniform float ortho_amount : hint_range(0.0, 1.0) = 0.7;
uniform float plane_distance = 4.0;

uniform vec4 outline_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);

void vertex() {
    vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    vec4 clip = PROJECTION_MATRIX * view_pos;

    float x_scale = PROJECTION_MATRIX[0][0] / plane_distance;
    float ortho_ndc_x = view_pos.x * x_scale;

    clip.x = mix(clip.x, ortho_ndc_x * clip.w, ortho_amount);
    POSITION = clip;
}

void fragment() {
    ALBEDO = outline_color.rgb;
}
