shader_type spatial;

// Flat, unshaded look (your "pure emission/unlit" goal). The important part is vertex(): it makes
// the character project as a blend of orthographic + perspective on the HORIZONTAL axis only, so
// characters don't look angled when they slide toward the screen edges. Vertical stays perspective.
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

void vertex() {
    vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    vec4 clip = PROJECTION_MATRIX * view_pos;

    float x_scale = PROJECTION_MATRIX[0][0] / plane_distance;
    float ortho_ndc_x = view_pos.x * x_scale;

    clip.x = mix(clip.x, ortho_ndc_x * clip.w, ortho_amount);
    POSITION = clip;
}

void fragment() {
    vec3 c = albedo_color.rgb;
    if (use_texture) {
        c *= texture(albedo_texture, UV).rgb;
    }
    ALBEDO = c;
}
