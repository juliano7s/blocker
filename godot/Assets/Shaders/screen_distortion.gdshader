shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float global_intensity : hint_range(0.0, 1.0) = 1.0;

// Up to 8 simultaneous distortion sources
// Each source: vec4(screen_x, screen_y, intensity, time_remaining_normalized)
uniform vec4 sources[8];
uniform int source_count = 0;

uniform float ripple_frequency : hint_range(5.0, 40.0) = 18.0;
uniform float ripple_speed : hint_range(1.0, 10.0) = 4.0;
uniform float max_displacement : hint_range(0.0, 0.05) = 0.012;

void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 total_offset = vec2(0.0);

    for (int i = 0; i < 8; i++) {
        if (i >= source_count) break;

        vec2 source_pos = sources[i].xy;
        float strength = sources[i].z;
        float life = sources[i].w; // 1.0 = just started, 0.0 = expired

        vec2 delta = uv - source_pos;
        float dist = length(delta);
        vec2 dir = dist > 0.001 ? normalize(delta) : vec2(0.0);

        // Expanding ring ripple
        float ring_radius = (1.0 - life) * 0.3; // ring expands outward
        float ring_dist = abs(dist - ring_radius);
        float ring = exp(-ring_dist * ripple_frequency) * sin(ring_dist * ripple_frequency * 6.28 - TIME * ripple_speed);

        // Fade with distance and lifetime
        float fade = smoothstep(0.4, 0.0, dist) * life;

        total_offset += dir * ring * strength * max_displacement * fade;
    }

    uv += total_offset * global_intensity;
    COLOR = texture(screen_texture, uv);
}
