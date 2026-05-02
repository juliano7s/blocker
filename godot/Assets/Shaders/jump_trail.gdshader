// Jump trail shader — parameterized for multiple trail styles, layered rendering.
// Modes: 0=energy ribbon, 1=afterimage, 2=warp distortion, 3=spark scatter.
shader_type canvas_item;
render_mode blend_add;

uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms   : hint_range(0.0, 100000.0) = 0.0;

uniform vec4 trail_color : source_color = vec4(0.3, 0.7, 1.0, 1.0);
uniform float brightness : hint_range(0.1, 3.0) = 1.0;
uniform int mode : hint_range(0, 3) = 0;
uniform float trail_width : hint_range(0.05, 0.5) = 0.2;
uniform float shimmer_speed : hint_range(0.0, 15.0) = 5.0;
uniform float dissolve_rate : hint_range(0.5, 3.0) = 1.0;
uniform float detail_scale : hint_range(1.0, 10.0) = 4.0;
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

float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    mat2 rot = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
    for (int i = 0; i < 5; i++) {
        v += a * value_noise(p); p = rot * p * 2.1; a *= 0.5;
    }
    return v;
}

// ─── Mode 0: Energy Ribbon ──────────────────────────────────────
vec4 energy_ribbon(float x, float y) {
    float t = age_ms * shimmer_speed * 0.001;
    float tail_fade = clamp(1.0 - progress * dissolve_rate * (1.0 - x * 0.8), 0.0, 1.0);
    tail_fade *= tail_fade; // quadratic

    // Soft taper at both ends so there's no hard edge
    float end_taper = smoothstep(0.0, 0.08, x) * smoothstep(0.0, 0.08, 1.0 - x);
    tail_fade *= end_taper;

    // Wavy displacement
    float wave = sin(x * detail_scale * 8.0 + t * 2.5) * 0.3
               + sin(x * detail_scale * 13.0 - t * 1.8) * 0.15
               + sin(x * detail_scale * 21.0 + t * 3.2) * 0.08;
    float center = wave * trail_width * 0.3;
    float dist = abs(y - center);
    float w = trail_width * tail_fade;

    if (dist > w * 1.5) return vec4(0.0);

    // Layered intensities
    float inner = exp(-dist * dist / (w * w * 0.03));
    float mid = exp(-dist * dist / (w * w * 0.12));
    float outer = exp(-dist * dist / (w * w * 0.5));

    // Shimmer sparkles
    float sparkle = value_noise(vec2(x * 25.0 + t * 1.5, y * 20.0 - t));
    sparkle = pow(max(sparkle - 0.5, 0.0) * 2.0, 4.0) * mid * 0.6;

    // Internal flowing texture
    float flow = fbm(vec2(x * detail_scale * 4.0 - t * 2.0, y * detail_scale * 3.0 + t * 0.3));
    float texture_detail = flow * 0.3 * mid;

    vec3 col;
    float val;
    if (is_core) {
        col = mix(vec3(1.0), trail_color.rgb, 0.3);
        val = inner * 0.9 + mid * 0.3 + sparkle + texture_detail;
    } else {
        col = trail_color.rgb;
        val = outer * 0.5 + mid * 0.15 + sparkle * 0.3;
    }

    val *= brightness * tail_fade;
    if (val < 0.005) return vec4(0.0);
    return vec4(col, clamp(val, 0.0, 1.0));
}

// ─── Mode 1: Ghost Afterimages ──────────────────────────────────
vec4 ghost_afterimage(float x, float y) {
    float t = age_ms * 0.001;
    float block_w = 0.055;
    float block_h = trail_width * 0.65;

    float alpha = 0.0;
    vec3 col = vec3(0.0);

    for (float i = 0.0; i < 7.0; i += 1.0) {
        float frac_pos = (i + 0.5) / 7.0;
        float dx = abs(x - frac_pos);
        float dy = abs(y);

        if (dx > block_w * 1.5 || dy > block_h * 1.5) continue;

        // Ghost opacity: front ghosts brighter
        float ghost_alpha = 0.15 + frac_pos * 0.55;
        // Dissolve over time, tail first
        ghost_alpha *= clamp(1.0 - progress * dissolve_rate * (1.0 - frac_pos * 0.8), 0.0, 1.0);
        if (ghost_alpha < 0.01) continue;

        if (is_core) {
            // Sharp block shape
            float sx = smoothstep(block_w, block_w * 0.7, dx);
            float sy = smoothstep(block_h, block_h * 0.7, dy);
            float shape = sx * sy;

            // Glowing border
            float border_x = smoothstep(block_w * 0.9, block_w * 0.6, dx) - smoothstep(block_w * 0.6, block_w * 0.3, dx);
            float border_y = smoothstep(block_h * 0.9, block_h * 0.6, dy) - smoothstep(block_h * 0.6, block_h * 0.3, dy);
            float border = max(border_x * smoothstep(block_h, block_h * 0.5, dy),
                             border_y * smoothstep(block_w, block_w * 0.5, dx));

            float a = (shape * 0.6 + border * 0.8) * ghost_alpha;
            vec3 c = mix(trail_color.rgb, vec3(1.0), shape * 0.3 + border * 0.5);
            col = mix(col, c, clamp(a, 0.0, 1.0));
            alpha = max(alpha, a);
        } else {
            // Soft glow around block
            float glow = exp(-(dx * dx + dy * dy) / (block_w * block_h * 4.0));
            float a = glow * ghost_alpha * 0.5;
            col = mix(col, trail_color.rgb, clamp(a, 0.0, 1.0));
            alpha = max(alpha, a);
        }
    }

    alpha *= brightness;
    return vec4(col, clamp(alpha, 0.0, 1.0));
}

// ─── Mode 2: Warp Distortion ────────────────────────────────────
vec4 warp_distortion(float x, float y) {
    float t = age_ms * shimmer_speed * 0.001;
    float tail_fade = clamp(1.0 - progress * dissolve_rate * (1.0 - x * 0.7), 0.0, 1.0);
    tail_fade *= tail_fade;

    // Displacement field
    float warp_x = fbm(vec2(x * detail_scale * 2.0 + t * 0.7, y * 6.0 - t * 0.4));
    float warp_y = fbm(vec2(x * detail_scale * 2.5 - t * 0.5, y * 8.0 + t * 0.6));
    float displacement = (warp_x - 0.5) * trail_width;

    float dist = abs(y - displacement * 0.4);
    float w = trail_width * tail_fade;

    if (dist > w * 2.0) return vec4(0.0);

    // Rift core
    float rift = exp(-dist * dist / (w * w * 0.02));
    // Energy field
    float field = exp(-dist * dist / (w * w * 0.3));

    // Edge crackling — sharp noise at boundaries
    float crack_noise = value_noise(vec2(x * 35.0 + t * 5.0, y * 25.0 - t * 3.0));
    float crack = pow(max(crack_noise - 0.45, 0.0) / 0.55, 2.0) * field * 0.8;

    // Vertical distortion lines
    float distort = value_noise(vec2(x * 20.0 + t * 2.0, y * 3.0));
    float lines = pow(max(distort - 0.4, 0.0) / 0.6, 2.0) * field * 0.3;

    vec3 col;
    float val;
    if (is_core) {
        col = mix(trail_color.rgb, vec3(1.0), rift * 0.7 + crack * 0.5);
        val = rift * 0.9 + crack + lines;
    } else {
        col = trail_color.rgb * 0.7;
        val = field * 0.4 + crack * 0.2 + lines * 0.15;
    }

    val *= brightness * tail_fade;
    if (val < 0.005) return vec4(0.0);
    return vec4(col, clamp(val, 0.0, 1.0));
}

// ─── Mode 3: Spark Scatter ──────────────────────────────────────
vec4 spark_scatter(float x, float y) {
    float t = age_ms * 0.001;
    float val = 0.0;
    vec3 col = vec3(0.0);

    for (float i = 0.0; i < 25.0; i += 1.0) {
        float sx = hash(vec2(i, 0.0));
        float sy = (hash(vec2(i, 1.0)) - 0.5) * trail_width * 2.5;

        // Drift
        float drift_x = (hash(vec2(i, 2.0)) - 0.5) * 0.15 * progress;
        float drift_y = (hash(vec2(i, 3.0)) - 0.4) * 0.08 * progress;
        sx += drift_x;
        sy += drift_y;

        float spark_life = clamp(1.0 - progress * dissolve_rate * (1.0 - sx * 0.5), 0.0, 1.0);
        if (spark_life < 0.01) continue;

        float dx = x - sx;
        float dy = y - sy;
        float d2 = dx * dx + dy * dy;

        float spark_radius = is_core ? 0.0004 : 0.002;
        spark_radius *= (0.7 + hash(vec2(i, 4.0)) * 0.6);

        if (d2 > spark_radius * 4.0) continue;

        float spark_val;
        if (is_core) {
            spark_val = exp(-d2 / (spark_radius * 0.3));
        } else {
            spark_val = exp(-d2 / (spark_radius * 1.5));
        }

        float flicker_val = 0.6 + 0.4 * sin(t * 12.0 + i * 7.0);
        spark_val *= spark_life * flicker_val;

        vec3 sc = mix(trail_color.rgb, vec3(1.0, 0.95, 0.85), spark_val * 0.6);
        col = mix(col, sc, clamp(spark_val, 0.0, 1.0));
        val = max(val, spark_val);
    }

    val *= brightness;
    if (val < 0.005) return vec4(0.0);
    return vec4(col, clamp(val, 0.0, 1.0));
}

void fragment() {
    float x = UV.x;
    float y = UV.y - 0.5;

    vec4 result;
    if (mode == 0) result = energy_ribbon(x, y);
    else if (mode == 1) result = ghost_afterimage(x, y);
    else if (mode == 2) result = warp_distortion(x, y);
    else result = spark_scatter(x, y);

    if (result.a < 0.005) discard;
    COLOR = result;
}
