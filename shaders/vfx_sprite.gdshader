shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, unshaded;

// Sprite VFX whose fade SUBTRACTS from every pixel's alpha instead of scaling it. Dropping fade by
// 1% removes 1% of alpha from every pixel, so faint pixels reach zero and vanish while the ones
// that survive stay partially transparent. Multiplying (the default) instead shrinks every pixel
// proportionally, so nothing ever drops out and the shape never changes.
//
// FrameVfx sets "fade"; it also binds "tex" from the Sprite3D's own texture and duplicates this
// material per sprite, so instances don't overwrite each other's fade.

uniform sampler2D tex : source_color, filter_linear;
uniform float fade : hint_range(0.0, 1.0) = 1.0;
uniform vec4 tint : source_color = vec4(1.0);

void vertex() {
    // Face the camera, keeping the node's scale (FrameVfx animates it).
    mat4 mv = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    mv[0] *= length(MODEL_MATRIX[0].xyz);
    mv[1] *= length(MODEL_MATRIX[1].xyz);
    mv[2] *= length(MODEL_MATRIX[2].xyz);
    MODELVIEW_MATRIX = mv;
}

void fragment() {
    vec4 c = texture(tex, UV) * tint;
    ALBEDO = c.rgb;
    ALPHA = clamp(c.a - (1.0 - fade), 0.0, 1.0);
}
