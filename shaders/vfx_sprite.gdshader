shader_type spatial;
render_mode blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, unshaded;
// depth_test_disabled: VFX draw over the stage and characters whatever the depth says.
// They still sort among themselves and stay under the camera-parented HUD sprites, which
// are nearer the camera and so drawn later in the transparent pass.

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
    // Face the camera, keeping the node's scale and its spin about Z. Plain billboarding builds the
    // orientation purely from the camera and so discards all node rotation; recovering the roll and
    // reapplying it in the camera's plane is what lets the sprite still be turned.
    float roll = atan(MODEL_MATRIX[0].y, MODEL_MATRIX[0].x);
    float cr = cos(roll);
    float sr = sin(roll);

    vec3 right = INV_VIEW_MATRIX[0].xyz;
    vec3 up = INV_VIEW_MATRIX[1].xyz;

    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
        vec4((right * cr + up * sr) * length(MODEL_MATRIX[0].xyz), 0.0),
        vec4((up * cr - right * sr) * length(MODEL_MATRIX[1].xyz), 0.0),
        vec4(INV_VIEW_MATRIX[2].xyz * length(MODEL_MATRIX[2].xyz), 0.0),
        MODEL_MATRIX[3]);
}

void fragment() {
    vec4 c = texture(tex, UV) * tint;
    ALBEDO = c.rgb;
    ALPHA = clamp(c.a - (1.0 - fade), 0.0, 1.0);
}
