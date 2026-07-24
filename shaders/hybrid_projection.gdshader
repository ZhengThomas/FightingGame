shader_type spatial;

// Flat, unshaded look (your "pure emission/unlit" goal). The important part is vertex(): it makes
// the character project as a blend of orthographic + perspective on BOTH axes, so characters don't
// look angled/foreshortened when they move toward the screen edges or up/down.
//
// cull_disabled matches how your body materials were imported (double-sided).
render_mode unshaded, cull_disabled;

// 0.0 = full perspective, 1.0 = fully orthographic (horizontal only). ASW use ~0.7.
uniform float ortho_amount : hint_range(0.0, 1.0) = 0.7;
// Distance from the camera to the plane the fight happens on (your camera sits ~4 units away).
uniform float plane_distance = 4.0;

// The surface's flat color, and optionally a texture (used only if use_texture is on).
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, hint_default_white;
uniform bool use_texture = false;

// Draw-order nudge: shifts ONLY the sorting depth (not screen position/size), so the "front"
// character wins the depth buffer where the two models overlap. 
uniform float depth_bias = 0.0;

void vertex() {
    vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    vec4 clip = PROJECTION_MATRIX * view_pos;

    // Orthographic projection is like perspective but with the real depth swapped for a fixed
    // plane_distance. Blend that in on both axes by ortho_amount.
    float ortho_ndc_x = view_pos.x * (PROJECTION_MATRIX[0][0] / plane_distance);
    float ortho_ndc_y = view_pos.y * (PROJECTION_MATRIX[1][1] / plane_distance);

    clip.x = mix(clip.x, ortho_ndc_x * clip.w, ortho_amount);
    clip.y = mix(clip.y, ortho_ndc_y * clip.w, ortho_amount);
    clip.z += depth_bias * clip.w; // reverse-Z: + = nearer. Only affects sorting, not size.
    POSITION = clip;
}

void fragment() {
    vec3 c = albedo_color.rgb;
    if (use_texture) {
        c *= texture(albedo_texture, UV).rgb;
    }
    ALBEDO = c;
}
