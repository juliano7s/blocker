// Laser beam shader — multi-purpose layer (core, mid, glow).
// Stack 3 ColorRects with different width/brightness for rich layered look.
shader_type canvas_item;
render_mode blend_add;

uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms   : hint_range(0.0, 100000.0) = 0.0;

uniform vec4 beam_color : source_color = vec4(0.3, 0.7, 1.0, 1.0);
uniform float brightness : hint_range(0.1, 3.0) = 1.0;
uniform float edge_softness : hint_range(0.1, 1.0) = 0.5;
uniform float taper_tip : hint_range(0.01, 0.2) = 0.06;
uniform float pulse_speed : hint_range(0.0, 20.0) = 0.0;
uniform float pulse_amp : hint_range(0.0, 0.5) = 0.0;
uniform float flare_size : hint_range(0.0, 0.2) = 0.04;
uniform bool is_core = false;

void fragment() {
    float x = UV.x;
    float y = UV.y - 0.5;
    float dist = abs(y);

    // Head travels forward
    float head = clamp(progress * 1.6, 0.0, 1.0);
    if (x > head) discard;

    // Fade out phase
    float fade_out = progress > 0.65 ? 1.0 - (progress - 0.65) / 0.35 : 1.0;
    fade_out = fade_out * fade_out; // quadratic for punchier pop

    // Taper at source
    float taper = smoothstep(0.0, 0.04, x);
    // Taper at tip
    float tip_dist = head - x;
    taper *= smoothstep(0.0, taper_tip, tip_dist);

    // Pulse
    float pulse = 1.0 + pulse_amp * sin(x * 50.0 - age_ms * pulse_speed * 0.001);

    // Beam profile — distance from center
    float half_width = 0.5 * taper * pulse;
    if (dist > half_width) discard;

    float profile = 1.0 - dist / half_width;
    // Edge softness control
    profile = pow(profile, edge_softness);

    // White-hot center for core layer
    vec3 col = beam_color.rgb;
    if (is_core) {
        col = mix(col, vec3(1.0), profile * 0.8);
        // Extra bright center line
        float center_line = exp(-dist * dist / (half_width * half_width * 0.01));
        col = mix(col, vec3(1.0), center_line * 0.5);
    }

    // Tip flare — bright flash at the leading edge
    float flare = 0.0;
    if (tip_dist < flare_size) {
        flare = (1.0 - tip_dist / flare_size);
        flare = flare * flare * 0.8;
        col = mix(col, vec3(1.0), flare);
    }

    // Shimmer
    float shimmer = 0.85 + 0.15 * sin(age_ms * 0.01 + x * 30.0);

    float alpha = profile * brightness * fade_out * shimmer + flare * fade_out;
    alpha = clamp(alpha, 0.0, 1.0);

    if (alpha < 0.005) discard;
    COLOR = vec4(col, alpha);
}
