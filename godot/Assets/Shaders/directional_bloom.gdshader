shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float threshold : hint_range(0.0, 1.5) = 0.55;
uniform float streak_intensity : hint_range(0.0, 1.0) = 0.3;
uniform float streak_length : hint_range(0.0, 0.05) = 0.015;
uniform int samples : hint_range(4, 24) = 12;
uniform float angle_degrees : hint_range(-90.0, 90.0) = 0.0;
uniform float anamorphic_ratio : hint_range(0.0, 1.0) = 0.8;

void fragment() {
    vec4 scene = texture(screen_texture, SCREEN_UV);

    // Streak direction — default horizontal, rotatable
    float angle = radians(angle_degrees);
    vec2 dir = vec2(cos(angle), sin(angle) * (1.0 - anamorphic_ratio));

    // Accumulate bright pixels along streak direction
    vec3 streak = vec3(0.0);
    float total_weight = 0.0;

    for (int i = -samples; i <= samples; i++) {
        if (i == 0) continue;
        float fi = float(i) / float(samples);
        vec2 offset = dir * fi * streak_length;
        vec4 s = texture(screen_texture, SCREEN_UV + offset);

        // Extract bright parts
        float luma = dot(s.rgb, vec3(0.299, 0.587, 0.114));
        float bright = max(luma - threshold, 0.0);

        // Falloff from center
        float weight = bright * (1.0 - abs(fi));
        streak += s.rgb * weight;
        total_weight += weight;
    }

    if (total_weight > 0.0) {
        streak /= total_weight;
    }

    vec3 color = scene.rgb + streak * streak_intensity;
    COLOR = vec4(color, 1.0);
}
