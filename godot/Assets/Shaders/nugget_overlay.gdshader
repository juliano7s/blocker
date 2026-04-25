shader_type canvas_item;
render_mode blend_add;

// Data texture: R = cell type (0=empty, 1=unmined nugget, 2=fortified wall)
//               G = mining progress (nuggets) or FortifiedHp (walls, 1-3)
//               B = block ID low byte (phase offset)
uniform sampler2D nugget_data : filter_nearest, repeat_disable;
uniform ivec2 grid_size;
uniform float cell_size = 28.0;
uniform float grid_padding = 140.0;

// Diamond distance field: returns 0 at center, 1 at diamond edge
float diamond_df(vec2 p, vec2 center, float size) {
    vec2 d = abs(p - center);
    return (d.x + d.y) / size;
}

// 4-pointed star sparkle — cross pattern
float star4(vec2 p, vec2 center, float arm_len, float arm_w) {
    vec2 d = abs(p - center);
    float h = step(d.y, arm_w) * step(d.x, arm_len);
    float v = step(d.x, arm_w) * step(d.y, arm_len);
    return max(h, v);
}

void fragment() {
    vec2 total_size = vec2(grid_size) * cell_size + grid_padding * 2.0;
    vec2 local_pos = UV * total_size;

    // Outside grid area — discard
    if (local_pos.x < grid_padding || local_pos.y < grid_padding ||
        local_pos.x > total_size.x - grid_padding || local_pos.y > total_size.y - grid_padding) {
        discard;
    }

    vec2 grid_local = local_pos - grid_padding;
    ivec2 cell_coords = ivec2(floor(grid_local / cell_size));
    cell_coords = clamp(cell_coords, ivec2(0), grid_size - 1);

    vec2 tex_uv = (vec2(cell_coords) + 0.5) / vec2(grid_size);
    vec4 data = texture(nugget_data, tex_uv);
    int cell_type = int(round(data.r * 255.0));
    int cell_g = int(round(data.g * 255.0));
    float phase = data.b * 255.0;

    if (cell_type == 0) discard;

    vec2 cell_origin = vec2(cell_coords) * cell_size;
    vec2 p = grid_local - cell_origin; // position within cell [0, cell_size]
    vec2 center = vec2(cell_size * 0.5);
    float cs = cell_size;

    vec3 col = vec3(0.0);
    float alpha = 0.0;

    if (cell_type == 1) {
        // === UNMINED NUGGET ===
        float diamond_size = cs * 0.30;
        float df = diamond_df(p, center, diamond_size);

        // Diamond fill — prismatic hue shift
        float hue_shift = sin(TIME * 1.2 + phase * 0.0145) * 0.08;
        float hue = 0.55 + hue_shift;
        // HSV to RGB (simplified for narrow hue range around cyan)
        vec3 diamond_rgb = vec3(
            0.5 + 0.5 * cos(6.2832 * (hue - 0.0)),
            0.5 + 0.5 * cos(6.2832 * (hue - 0.333)),
            0.5 + 0.5 * cos(6.2832 * (hue - 0.666))
        );
        diamond_rgb = mix(vec3(1.0), diamond_rgb, 0.08); // very low saturation

        if (df < 1.0) {
            float fill_alpha = 0.6 * (1.0 - df * 0.3);
            col = diamond_rgb;
            alpha = fill_alpha;
        }

        // Diamond stroke
        if (df > 0.85 && df < 1.15) {
            float stroke_a = 0.8 * (1.0 - abs(df - 1.0) / 0.15);
            col = mix(col, vec3(0.7, 0.75, 0.85), stroke_a);
            alpha = max(alpha, stroke_a * 0.8);
        }

        // Ambient radial glow
        float glow_dist = length(p - center) / (cs * 0.45);
        if (glow_dist < 1.0) {
            float glow_a = 0.20 * (1.0 - glow_dist);
            col = mix(col, vec3(0.85, 0.9, 1.0), glow_a / max(alpha + glow_a, 0.001));
            alpha = max(alpha, glow_a);
        }

        // 4 prismatic star sparkles
        vec3 star_colors[4] = vec3[4](
            vec3(0.67, 0.87, 1.0),  // ice blue
            vec3(1.0, 0.83, 1.0),   // pink
            vec3(0.83, 1.0, 0.87),  // green
            vec3(1.0, 1.0, 1.0)     // white
        );

        for (int i = 0; i < 4; i++) {
            float fi = float(i);
            float sp = TIME * 1.5 + fi * 2.1 + phase * 0.051;
            float sparkle_alpha = max(0.0, sin(sp)) * 0.9;

            if (sparkle_alpha > 0.05) {
                float sx = center.x + sin(sp * 0.7 + fi * 4.2) * cs * 0.3;
                float sy = center.y + cos(sp * 0.5 + fi * 3.1) * cs * 0.3;
                float arm_len = 2.5 + sparkle_alpha * 1.5;
                float arm_w = 0.6;
                float s = star4(p, vec2(sx, sy), arm_len, arm_w);
                if (s > 0.0) {
                    col = mix(col, star_colors[i], s * sparkle_alpha / max(alpha + s * sparkle_alpha, 0.001));
                    alpha = max(alpha, s * sparkle_alpha);
                }
            }
        }

        // Mining progress cracks (cell_g > 0 means being mined)
        if (cell_g > 0) {
            float progress = float(cell_g) / 255.0;
            float crack_len = cs * 0.35 * progress;
            float crack_a = 0.4 + 0.5 * progress;

            // 4 radial crack lines from center
            vec2 crack_dirs[4] = vec2[4](
                vec2(1.0, -0.6), vec2(-0.8, 0.4), vec2(0.5, 0.7), vec2(-0.6, -0.9)
            );
            for (int i = 0; i < 4; i++) {
                if (i == 3 && progress < 0.4) continue;
                vec2 dir = normalize(crack_dirs[i]);
                vec2 to_p = p - center;
                float proj = dot(to_p, dir);
                float perp = length(to_p - dir * proj);
                if (proj > 0.0 && proj < crack_len && perp < 0.8) {
                    col = mix(col, vec3(1.0), crack_a);
                    alpha = max(alpha, crack_a);
                }
            }

            // Intensified shimmer during mining
            float shimmer = 0.15 + 0.2 * progress;
            float glow_r = length(p - center) / (cs * 0.5);
            if (glow_r < 1.0) {
                alpha = max(alpha, shimmer * (1.0 - glow_r));
                col = mix(col, vec3(1.0), shimmer * 0.5);
            }
        }
    }
    else if (cell_type == 2) {
        // === FORTIFIED WALL ===
        int hp = cell_g;
        float d_size = cs * 0.15;
        float pulse = 0.4 + 0.3 * sin(TIME * 2.5 + phase * 0.067);

        // Diamond positions based on HP
        vec2 positions[3];
        int count = 0;

        if (hp == 3) {
            // Inverted triangle
            positions[0] = center + vec2(-cs * 0.18, -cs * 0.12);
            positions[1] = center + vec2( cs * 0.18, -cs * 0.12);
            positions[2] = center + vec2(0.0,         cs * 0.15);
            count = 3;
        } else if (hp == 2) {
            // Vertical pair
            positions[0] = center + vec2(0.0, -cs * 0.12);
            positions[1] = center + vec2(0.0,  cs * 0.12);
            count = 2;
        } else if (hp >= 1) {
            // Single center
            positions[0] = center;
            count = 1;
        }

        for (int i = 0; i < count; i++) {
            float df = diamond_df(p, positions[i], d_size);

            // Diamond fill
            if (df < 1.0) {
                float fill_a = pulse * (1.0 - df * 0.3);
                col = mix(col, vec3(0.7, 0.85, 1.0), fill_a / max(alpha + fill_a, 0.001));
                alpha = max(alpha, fill_a);
            }

            // Diamond stroke
            if (df > 0.8 && df < 1.2) {
                float stroke_a = pulse * 0.6 * (1.0 - abs(df - 1.0) / 0.2);
                col = mix(col, vec3(1.0), stroke_a / max(alpha + stroke_a, 0.001));
                alpha = max(alpha, stroke_a);
            }

            // Per-diamond sparkle star
            float sp = TIME * 1.8 + float(i) * 2.3 + phase * 0.043;
            float sparkle_a = max(0.0, sin(sp)) * 0.7;
            if (sparkle_a > 0.05) {
                float sx = positions[i].x + sin(sp * 0.6 + float(i) * 3.7) * d_size * 0.8;
                float sy = positions[i].y + cos(sp * 0.4 + float(i) * 2.9) * d_size * 0.8;
                float s = star4(p, vec2(sx, sy), 2.0, 0.5);
                if (s > 0.0) {
                    col = mix(col, vec3(0.8, 0.9, 1.0), s * sparkle_a / max(alpha + s * sparkle_a, 0.001));
                    alpha = max(alpha, s * sparkle_a);
                }
            }
        }
    }

    if (alpha < 0.005) discard;
    COLOR = vec4(col, alpha);
}
