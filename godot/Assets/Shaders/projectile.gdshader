// Projectile shader — traveling energy ball/bolt with trailing wake.
// Rendered on a ColorRect. The projectile moves from left to right.
// Modes: 0=energy ball, 1=lightning orb, 2=fireball, 3=comet
shader_type canvas_item;
render_mode blend_add;

uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms   : hint_range(0.0, 100000.0) = 0.0;

uniform vec4 proj_color : source_color = vec4(0.3, 0.7, 1.0, 1.0);
uniform float brightness : hint_range(0.1, 4.0) = 1.5;
uniform int mode : hint_range(0, 4) = 0;
uniform float proj_size : hint_range(0.02, 0.15) = 0.06;
uniform float trail_length : hint_range(0.0, 0.6) = 0.25;
uniform float trail_width : hint_range(0.3, 1.0) = 0.6;
uniform float turbulence : hint_range(0.0, 1.5) = 0.5;
uniform float flicker_speed : hint_range(0.0, 20.0) = 8.0;
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

float noise1d(float x, float seed) {
    float i = floor(x);
    float f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(hash(vec2(i, seed)), hash(vec2(i + 1.0, seed)), f) * 2.0 - 1.0;
}

// ─── Mode 0: Energy Ball ────────────────────────────────────────
vec4 energy_ball(float x, float y, float head_x) {
    float t = age_ms * 0.001;

    // Distance from projectile center
    float dx = x - head_x;
    float dy = y;
    float dist = sqrt(dx * dx + dy * dy);

    float val = 0.0;
    vec3 col = vec3(0.0);

    // Core orb — pulsing
    float pulse = 1.0 + 0.15 * sin(t * 12.0);
    float orb_r = proj_size * pulse;

    if (is_core) {
        // Tight bright core
        float core = exp(-dist * dist / (orb_r * orb_r * 0.15));
        // Ring detail
        float ring = exp(-pow(dist - orb_r * 0.6, 2.0) / (orb_r * orb_r * 0.02));
        val = core * 1.0 + ring * 0.4;
        col = mix(proj_color.rgb, vec3(1.0), core * 0.8);
    } else {
        // Wide glow halo
        float halo = exp(-dist * dist / (orb_r * orb_r * 0.8));
        // Outer shimmer
        float shimmer_noise = value_noise(vec2(atan(dy, dx) * 3.0 + t * 4.0, dist * 10.0));
        float shimmer = shimmer_noise * 0.3 * halo;
        val = halo * 0.6 + shimmer;
        col = proj_color.rgb;
    }

    // Trailing wake
    if (dx < 0.0 && abs(dx) < trail_length) {
        float trail_t = -dx / trail_length;
        float trail_fade = (1.0 - trail_t);
        trail_fade = trail_fade * trail_fade;
        float trail_r = orb_r * trail_width * (1.0 - trail_t * 0.5);
        float trail_dist = abs(dy);

        if (is_core) {
            float tv = exp(-trail_dist * trail_dist / (trail_r * trail_r * 0.1)) * trail_fade * 0.5;
            val = max(val, tv);
        } else {
            float tv = exp(-trail_dist * trail_dist / (trail_r * trail_r * 0.6)) * trail_fade * 0.35;
            val = max(val, tv);
        }
    }

    val *= brightness;
    if (val < 0.005) return vec4(0.0);
    return vec4(col, clamp(val, 0.0, 1.0));
}

// ─── Mode 1: Lightning Orb ──────────────────────────────────────
vec4 lightning_orb(float x, float y, float head_x) {
    float t = age_ms * 0.001;
    float t_seed = floor(t * 8.0);

    float dx = x - head_x;
    float dy = y;
    float dist = sqrt(dx * dx + dy * dy);
    float angle = atan(dy, dx);

    float val = 0.0;
    vec3 col = vec3(0.0);

    float orb_r = proj_size;

    if (is_core) {
        // Bright crackling core
        float core = exp(-dist * dist / (orb_r * orb_r * 0.08));

        // Electric tendrils radiating outward — re-randomize
        float tendril_val = 0.0;
        for (float i = 0.0; i < 6.0; i += 1.0) {
            float tendril_angle = i * 1.047 + sin(t * 3.0 + i) * 0.5;
            float angle_diff = abs(mod(angle - tendril_angle + PI, TAU) - PI);
            float angular_weight = exp(-angle_diff * angle_diff * 8.0);

            float tendril_r = orb_r * (1.5 + noise1d(i + t_seed, i * 13.7) * 0.8);
            float radial = 1.0 - clamp((dist - orb_r * 0.3) / (tendril_r - orb_r * 0.3), 0.0, 1.0);

            // Jagged edges
            float jag = noise1d(dist * 30.0 + i * 10.0, t_seed * 7.0 + i);
            radial *= (0.7 + jag * 0.5);

            tendril_val += angular_weight * radial * 0.6;
        }

        val = core * 1.0 + tendril_val;
        col = mix(proj_color.rgb, vec3(1.0), core * 0.8 + tendril_val * 0.3);
    } else {
        // Wide crackling glow
        float halo = exp(-dist * dist / (orb_r * orb_r * 1.2));
        float crackle = value_noise(vec2(angle * 5.0 + t * 6.0, dist * 15.0 + t_seed));
        crackle = pow(max(crackle - 0.4, 0.0) / 0.6, 2.0);
        val = halo * 0.5 + crackle * halo * 0.5;
        col = proj_color.rgb;
    }

    // Electric trailing sparks
    if (dx < 0.0 && abs(dx) < trail_length) {
        float trail_t = -dx / trail_length;
        float trail_fade = pow(1.0 - trail_t, 2.0);

        for (float si = 0.0; si < 8.0; si += 1.0) {
            float spark_x = head_x - trail_length * hash(vec2(si, t_seed));
            float spark_y = (hash(vec2(si, t_seed + 1.0)) - 0.5) * orb_r * 2.0;
            float sd = length(vec2(x - spark_x, y - spark_y));
            float spark_r = is_core ? 0.003 : 0.008;
            float sv = exp(-sd * sd / (spark_r * spark_r)) * trail_fade;
            val += sv * 0.4;
            col = mix(col, vec3(1.0), sv * 0.3);
        }
    }

    // Flicker
    float flicker = 0.75 + 0.25 * sin(t * flicker_speed + dist * 20.0);
    val *= flicker * brightness;

    if (val < 0.005) return vec4(0.0);
    return vec4(col, clamp(val, 0.0, 1.0));
}

// ─── Mode 2: Fireball ───────────────────────────────────────────
vec4 fireball(float x, float y, float head_x) {
    float t = age_ms * 0.001;

    float dx = x - head_x;
    float dy = y;
    float dist = sqrt(dx * dx + dy * dy);

    float val = 0.0;
    vec3 col = vec3(0.0);

    float orb_r = proj_size;

    // Turbulent boundary
    float angle = atan(dy, dx);
    float noise_r = fbm(vec2(angle * 3.0 + t * 4.0, dist * 8.0 - t * 2.0));
    float boundary = orb_r * (0.7 + noise_r * turbulence * 0.8);

    if (is_core) {
        float inner = exp(-dist * dist / (boundary * boundary * 0.08));
        float mid = exp(-dist * dist / (boundary * boundary * 0.25));

        // Surface fire detail
        float fire_detail = fbm(vec2(angle * 5.0 - t * 6.0, dist * 12.0 + t * 3.0));
        float surface = fire_detail * mid * 0.5;

        val = inner * 1.0 + mid * 0.3 + surface;
        // White-hot center → yellow → orange gradient
        vec3 white = vec3(1.0, 1.0, 0.95);
        vec3 yellow = vec3(1.0, 0.85, 0.3);
        col = mix(yellow, white, inner * 0.8);
        col = mix(proj_color.rgb, col, mid);
    } else {
        float outer = exp(-dist * dist / (boundary * boundary * 0.7));
        float haze = fbm(vec2(angle * 4.0 + t * 2.0, dist * 6.0 - t));
        val = outer * 0.5 + haze * outer * 0.3;
        col = proj_color.rgb;
    }

    // Fire trail — turbulent wake
    if (dx < 0.0 && abs(dx) < trail_length) {
        float trail_t = -dx / trail_length;
        float trail_fade = pow(1.0 - trail_t, 1.5);
        float trail_r = orb_r * trail_width * (1.0 + trail_t * 0.5);

        float fire_noise = fbm(vec2(x * 15.0 - t * 8.0, y * 10.0 + t * 2.0));
        float trail_boundary = trail_r * (0.5 + fire_noise * turbulence);
        float trail_dist = abs(dy);

        if (trail_dist < trail_boundary) {
            float tv = (1.0 - trail_dist / trail_boundary) * trail_fade;
            tv = tv * tv;

            if (is_core) {
                float ember = pow(max(fire_noise - 0.4, 0.0) / 0.6, 2.0) * tv * 0.6;
                val = max(val, tv * 0.4 + ember);
                col = mix(col, vec3(1.0, 0.7, 0.3), tv * 0.3);
            } else {
                val = max(val, tv * 0.3);
            }
        }
    }

    val *= brightness;
    if (val < 0.005) return vec4(0.0);
    return vec4(col, clamp(val, 0.0, 1.0));
}

// ─── Mode 3: Comet ──────────────────────────────────────────────
vec4 comet(float x, float y, float head_x) {
    float t = age_ms * 0.001;

    float dx = x - head_x;
    float dy = y;
    float dist = sqrt(dx * dx + dy * dy);

    float val = 0.0;
    vec3 col = vec3(0.0);

    float orb_r = proj_size * 0.7;

    if (is_core) {
        // Small intense head
        float core = exp(-dist * dist / (orb_r * orb_r * 0.06));
        float ring = exp(-pow(dist - orb_r * 0.5, 2.0) / (orb_r * orb_r * 0.015)) * 0.4;
        val = core + ring;
        col = mix(proj_color.rgb, vec3(1.0), core * 0.85);
    } else {
        float halo = exp(-dist * dist / (orb_r * orb_r * 0.5));
        val = halo * 0.5;
        col = proj_color.rgb;
    }

    // Long tapering tail — the signature comet look
    if (dx < 0.0 && abs(dx) < trail_length) {
        float trail_t = -dx / trail_length;
        float trail_fade = pow(1.0 - trail_t, 3.0); // cubic for long taper

        // Trail narrows toward the end
        float trail_r = orb_r * trail_width * (1.0 - trail_t * 0.8);
        float trail_dist = abs(dy);

        // Wispy noise
        float wisp = value_noise(vec2(x * 20.0 - t * 5.0, y * 15.0 + t));
        trail_r *= (0.7 + wisp * 0.6);

        if (trail_dist < trail_r) {
            float tv = exp(-trail_dist * trail_dist / (trail_r * trail_r * 0.3)) * trail_fade;

            if (is_core) {
                // Bright inner trail
                float inner_trail = exp(-trail_dist * trail_dist / (trail_r * trail_r * 0.05)) * trail_fade;
                val = max(val, inner_trail * 0.7 + tv * 0.2);
                col = mix(col, vec3(1.0), inner_trail * 0.4);
            } else {
                // Scattered particles in tail
                float particles = pow(max(wisp - 0.45, 0.0) / 0.55, 3.0) * trail_fade * 0.4;
                val = max(val, tv * 0.4 + particles);
            }
        }
    }

    val *= brightness;
    if (val < 0.005) return vec4(0.0);
    return vec4(col, clamp(val, 0.0, 1.0));
}

// ─── Mode 4: Lightning Comet ────────────────────────────────────
vec4 lightning_comet(float x, float y, float head_x) {
    float t = age_ms * 0.001;
    float t_seed = floor(t * flicker_speed * 0.8);

    float dx = x - head_x;
    float dy = y;
    float dist = sqrt(dx * dx + dy * dy);

    float val = 0.0;
    vec3 col = vec3(0.0);

    float orb_r = proj_size * 0.6;

    // ── Head: small crackling orb ──
    if (is_core) {
        float core = exp(-dist * dist / (orb_r * orb_r * 0.05));
        // Electric surface crackle
        float angle = atan(dy, dx);
        float crackle = 0.0;
        for (float i = 0.0; i < 5.0; i += 1.0) {
            float t_angle = i * 1.257 + sin(t * 4.0 + i * 2.0) * 0.8;
            float a_diff = abs(mod(angle - t_angle + PI, TAU) - PI);
            float aw = exp(-a_diff * a_diff * 6.0);
            float tr = orb_r * (1.2 + noise1d(i + t_seed, i * 11.3) * 0.5);
            float radial = max(0.0, 1.0 - (dist - orb_r * 0.2) / (tr - orb_r * 0.2));
            float jag = 0.7 + noise1d(dist * 25.0 + i * 8.0, t_seed * 5.0 + i) * 0.5;
            crackle += aw * radial * jag * 0.5;
        }
        val = core * 1.0 + crackle;
        col = mix(proj_color.rgb, vec3(1.0), core * 0.85 + crackle * 0.3);
    } else {
        float halo = exp(-dist * dist / (orb_r * orb_r * 0.6));
        float surface_crackle = value_noise(vec2(atan(dy, dx) * 4.0 + t * 5.0, dist * 12.0 + t_seed));
        surface_crackle = pow(max(surface_crackle - 0.35, 0.0) / 0.65, 2.0);
        val = halo * 0.5 + surface_crackle * halo * 0.4;
        col = proj_color.rgb;
    }

    // ── Trail: jagged lightning tail ──
    if (dx < 0.0 && abs(dx) < trail_length) {
        float trail_t = -dx / trail_length;
        float trail_fade = pow(1.0 - trail_t, 2.5);

        // Main bolt path — jagged center line
        float bolt_center = noise1d(trail_t * 15.0 + 0.5, t_seed * 3.7) * orb_r * trail_width * 1.5;
        bolt_center += noise1d(trail_t * 35.0, t_seed * 7.1) * orb_r * trail_width * 0.5;
        float bolt_dist = abs(dy - bolt_center);

        // Trail narrows
        float bolt_r = orb_r * trail_width * (1.0 - trail_t * 0.7);

        if (is_core) {
            // Sharp bright bolt
            float bolt_core = exp(-bolt_dist * bolt_dist / (bolt_r * bolt_r * 0.04)) * trail_fade;
            val = max(val, bolt_core * 0.8);
            col = mix(col, vec3(1.0), bolt_core * 0.5);

            // Small forks off the main bolt
            for (float fi = 0.0; fi < 3.0; fi += 1.0) {
                float fork_at = 0.2 + fi * 0.25;
                if (trail_t < fork_at || trail_t > fork_at + 0.15) continue;
                float fork_local = (trail_t - fork_at) / 0.15;
                float fork_y = bolt_center + (hash(vec2(fi, t_seed)) > 0.5 ? 1.0 : -1.0)
                    * fork_local * orb_r * 1.5;
                float fork_dist = abs(dy - fork_y);
                float fork_v = exp(-fork_dist * fork_dist / (bolt_r * bolt_r * 0.02))
                    * (1.0 - fork_local) * trail_fade * 0.5;
                val = max(val, fork_v);
                col = mix(col, vec3(0.9, 0.95, 1.0), fork_v * 0.3);
            }
        } else {
            // Wide glow around bolt path
            float bolt_glow = exp(-bolt_dist * bolt_dist / (bolt_r * bolt_r * 0.5)) * trail_fade;
            val = max(val, bolt_glow * 0.35);

            // Scattered sparks in the wake
            for (float si = 0.0; si < 6.0; si += 1.0) {
                float sx = head_x - trail_length * hash(vec2(si + 20.0, t_seed));
                float sy = bolt_center * hash(vec2(si + 30.0, t_seed + 1.0))
                    + (hash(vec2(si + 40.0, t_seed)) - 0.5) * orb_r;
                float sd = length(vec2(x - sx, y - sy));
                float sv = exp(-sd * sd / 0.00005) * trail_fade * 0.3;
                val += sv;
            }
        }
    }

    // Flicker
    float flicker = 0.7 + 0.3 * sin(t * flicker_speed + x * 25.0);
    float flicker2 = 0.85 + 0.15 * sin(t * flicker_speed * 0.7 + dy * 30.0);
    val *= flicker * flicker2 * brightness;

    if (val < 0.005) return vec4(0.0);
    return vec4(col, clamp(val, 0.0, 1.0));
}

void fragment() {
    float x = UV.x;
    float y = UV.y - 0.5;

    // Projectile head position
    float head_x = clamp(progress * 1.3, 0.0, 1.0);

    // Fade everything after projectile reaches end
    float fade = progress > 0.8 ? 1.0 - (progress - 0.8) / 0.2 : 1.0;

    vec4 result;
    if (mode == 0) result = energy_ball(x, y, head_x);
    else if (mode == 1) result = lightning_orb(x, y, head_x);
    else if (mode == 2) result = fireball(x, y, head_x);
    else if (mode == 3) result = comet(x, y, head_x);
    else result = lightning_comet(x, y, head_x);

    result.a *= fade * fade;
    if (result.a < 0.005) discard;
    COLOR = result;
}
