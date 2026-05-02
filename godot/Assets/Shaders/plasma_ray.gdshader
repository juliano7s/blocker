// Plasma / fire bolt shader — turbulent hot energy, layered for richness.
// Stack wide glow + narrow core layers.
shader_type canvas_item;
render_mode blend_add;

uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms   : hint_range(0.0, 100000.0) = 0.0;

uniform vec4 inner_color : source_color = vec4(1.0, 0.95, 0.8, 1.0);
uniform vec4 outer_color : source_color = vec4(1.0, 0.4, 0.1, 1.0);
uniform float brightness : hint_range(0.1, 3.0) = 1.0;
uniform float plasma_width : hint_range(0.05, 0.5) = 0.15;
uniform float turbulence : hint_range(0.0, 1.5) = 0.5;
uniform float speed : hint_range(0.5, 10.0) = 3.0;
uniform float detail : hint_range(1.0, 8.0) = 4.0;
uniform bool is_core = false;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(
        mix(hash(i), hash(i + vec2(1, 0)), f.x),
        mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x),
        f.y
    );
}

float fbm(vec2 p, int octaves) {
    float v = 0.0, a = 0.5;
    mat2 rot = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
    for (int i = 0; i < octaves; i++) {
        v += a * value_noise(p);
        p = rot * p * 2.1;
        a *= 0.5;
    }
    return v;
}

void fragment() {
    float x = UV.x;
    float y = UV.y - 0.5;

    float head = clamp(progress * 1.6, 0.0, 1.0);
    if (x > head) discard;

    float fade_out = progress > 0.6 ? 1.0 - (progress - 0.6) / 0.4 : 1.0;
    fade_out = fade_out * fade_out;

    float t = age_ms * speed * 0.001;

    // Taper
    float taper = smoothstep(0.0, 0.06, x) * smoothstep(0.0, 0.05, head - x);

    // Turbulent center displacement
    float center_warp = (fbm(vec2(x * detail * 2.0 - t * 1.5, 0.5), 4) - 0.5) * turbulence * 0.3;

    // Distance from warped center
    float dy = y - center_warp;
    float dist = abs(dy);

    // Organic edge boundary — fbm-shaped
    float edge_noise = fbm(vec2(x * detail * 3.0 + t * 2.0, dy * 8.0 + t * 0.5), 5);
    float pw = plasma_width * taper * (0.6 + edge_noise * 0.8);

    if (dist > pw * 1.3) discard;

    // Layered intensity
    float inner_val = exp(-dist * dist / (pw * pw * 0.04)); // tight hot core
    float mid_val = exp(-dist * dist / (pw * pw * 0.15));    // medium glow
    float outer_val = exp(-dist * dist / (pw * pw * 0.5));   // wide halo

    // Noise detail on the surface
    float surface_noise = fbm(vec2(x * detail * 5.0 - t * 3.0, dy * detail * 4.0 + t), 4);
    float surface_detail = surface_noise * 0.4 * mid_val;

    // Color mixing
    float color_t = clamp(dist / pw, 0.0, 1.0);
    vec3 col;
    if (is_core) {
        col = mix(vec3(1.0), inner_color.rgb, color_t * 0.7);
        col = mix(col, outer_color.rgb, color_t * color_t);
        float val = inner_val * 1.0 + mid_val * 0.4 + surface_detail;
        val *= brightness * fade_out;
        // Flickering embers
        float ember = pow(max(surface_noise - 0.55, 0.0) / 0.45, 3.0) * mid_val * 0.5;
        val += ember;
        if (val < 0.005) discard;
        COLOR = vec4(col, clamp(val, 0.0, 1.0));
    } else {
        col = mix(outer_color.rgb, inner_color.rgb * 0.5, outer_val * 0.3);
        float val = outer_val * 0.5 + mid_val * 0.2 + surface_detail * 0.3;
        val *= brightness * fade_out;
        if (val < 0.005) discard;
        COLOR = vec4(col, clamp(val, 0.0, 1.0));
    }
}
