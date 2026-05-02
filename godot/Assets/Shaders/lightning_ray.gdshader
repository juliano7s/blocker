// Lightning bolt shader — jagged electrical arcs through continuous space.
// Stack multiple layers: wide dim glow + narrow bright core for richness.
shader_type canvas_item;
render_mode blend_add;

uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms   : hint_range(0.0, 100000.0) = 0.0;

uniform vec4 bolt_color : source_color = vec4(0.4, 0.7, 1.0, 1.0);
uniform float brightness : hint_range(0.1, 3.0) = 1.0;
uniform float bolt_thickness : hint_range(0.005, 0.1) = 0.02;
uniform float glow_falloff : hint_range(0.02, 0.5) = 0.12;
uniform float jag_amplitude : hint_range(0.0, 0.4) = 0.15;
uniform float jag_frequency : hint_range(2.0, 30.0) = 10.0;
uniform float fork_count : hint_range(0.0, 4.0) = 1.0;
uniform float fork_spread : hint_range(0.0, 0.3) = 0.12;
uniform float flicker_speed : hint_range(0.0, 30.0) = 8.0;
uniform float re_randomize_rate : hint_range(0.0, 20.0) = 6.0;
uniform bool is_core = false;

float hash(float n) {
    return fract(sin(n) * 43758.5453123);
}

float noise1d(float x, float seed) {
    float i = floor(x);
    float f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(hash(i + seed), hash(i + 1.0 + seed), f) * 2.0 - 1.0;
}

float bolt_path(float x, float seed_offset) {
    float t_seed = floor(age_ms * re_randomize_rate * 0.001) + seed_offset;
    float n1 = noise1d(x * jag_frequency, t_seed * 7.13);
    float n2 = noise1d(x * jag_frequency * 2.7, t_seed * 17.31) * 0.35;
    float n3 = noise1d(x * jag_frequency * 6.1, t_seed * 41.07) * 0.15;
    return (n1 + n2 + n3) * jag_amplitude;
}

float bolt_contribution(float y, float center, float thickness, float glow) {
    float d = abs(y - center);
    // Sharp bright core
    float core = exp(-d * d / (thickness * thickness));
    // Soft glow halo
    float halo = exp(-d * d / (glow * glow));
    if (is_core) {
        return core * 1.0 + halo * 0.3;
    } else {
        return halo * 0.6 + core * 0.15;
    }
}

void fragment() {
    float x = UV.x;
    float y = UV.y - 0.5;

    float head = clamp(progress * 1.8, 0.0, 1.0);
    if (x > head) discard;

    float fade_out = progress > 0.55 ? 1.0 - (progress - 0.55) / 0.45 : 1.0;
    fade_out = fade_out * fade_out;

    // Taper at tip
    float tip_taper = smoothstep(0.0, 0.06, head - x);
    float source_taper = smoothstep(0.0, 0.03, x);

    // Main bolt
    float main_center = bolt_path(x, 0.0);
    float val = bolt_contribution(y, main_center, bolt_thickness * tip_taper, glow_falloff * tip_taper);

    // Forks
    for (float fi = 1.0; fi <= 4.0; fi += 1.0) {
        if (fi > fork_count) break;
        float fork_start = 0.15 + hash(fi * 17.3 + floor(age_ms * re_randomize_rate * 0.001)) * 0.5;
        float fork_end = fork_start + 0.15 + hash(fi * 31.1) * 0.2;
        if (x < fork_start || x > min(fork_end, head)) continue;

        float fork_t = (x - fork_start) / (fork_end - fork_start);
        float fork_base = bolt_path(fork_start, 0.0);
        float fork_drift = fork_t * fork_spread * (hash(fi * 41.7 + floor(age_ms * re_randomize_rate * 0.001)) > 0.5 ? 1.0 : -1.0);
        float fork_jag = bolt_path(x, fi * 137.0) * 0.5;
        float fork_center = fork_base + fork_drift + fork_jag;
        float fork_fade = (1.0 - fork_t);

        val += bolt_contribution(y, fork_center, bolt_thickness * 0.7 * fork_fade, glow_falloff * 0.5 * fork_fade) * fork_fade * 0.7;
    }

    // Flicker
    float flicker = 0.7 + 0.3 * sin(age_ms * flicker_speed * 0.001 + x * 20.0);
    // Secondary flicker on different frequency
    float flicker2 = 0.85 + 0.15 * sin(age_ms * flicker_speed * 0.0007 + x * 8.0 + 2.0);

    val *= fade_out * flicker * flicker2 * source_taper * brightness;

    if (val < 0.005) discard;

    // Color: white-hot at high intensity, bolt_color at low
    vec3 col = mix(bolt_color.rgb, vec3(1.0), clamp(val * 0.6, 0.0, 0.8));

    COLOR = vec4(col, clamp(val, 0.0, 1.0));
}
