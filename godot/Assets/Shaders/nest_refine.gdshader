// Marching ants perimeter + interior sparkles for nest refine zone.
// CPU sets center + time uniforms. GPU computes per-pixel.
shader_type canvas_item;
render_mode blend_add;

uniform vec2 center = vec2(5.5, 5.5);
uniform vec2 grid_size = vec2(30.0, 30.0);
uniform float cell_size = 28.0;
uniform float max_radius = 3.0;
uniform float time_ms = 0.0;
uniform float march_speed = 40.0;
uniform vec4 zone_color : source_color = vec4(0.55, 0.67, 1.0, 0.8);

const float DASH_LEN = 8.0;
const float GAP_LEN = 5.0;
const float PATTERN_LEN = DASH_LEN + GAP_LEN;
const float LINE_HALF_W = 1.25;

const int SPARKLE_COUNT = 24;
const float SPARKLE_THRESHOLD = 0.3;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
    vec2 px = UV * grid_size * cell_size;
    vec2 gp = UV * grid_size;

    vec2 center_px = center * cell_size;
    vec2 local = px - center_px;

    float half_ext = max_radius * cell_size;

    // Distance to each rectangle edge
    float dist_left   = abs(local.x + half_ext);
    float dist_right  = abs(local.x - half_ext);
    float dist_top    = abs(local.y + half_ext);
    float dist_bottom = abs(local.y - half_ext);

    bool on_top    = dist_top    < LINE_HALF_W && abs(local.x) <= half_ext;
    bool on_bottom = dist_bottom < LINE_HALF_W && abs(local.x) <= half_ext;
    bool on_left   = dist_left   < LINE_HALF_W && abs(local.y) <= half_ext;
    bool on_right  = dist_right  < LINE_HALF_W && abs(local.y) <= half_ext;

    float alpha = 0.0;
    vec3 col = zone_color.rgb;

    if (on_top || on_bottom || on_left || on_right) {
        // Position along continuous perimeter loop
        float side_w = 2.0 * half_ext;
        float side_h = 2.0 * half_ext;
        float perimeter_pos = 0.0;

        if (on_top) {
            perimeter_pos = local.x + half_ext;
        } else if (on_right) {
            perimeter_pos = side_w + (local.y + half_ext);
        } else if (on_bottom) {
            perimeter_pos = side_w + side_h + (half_ext - local.x);
        } else if (on_left) {
            perimeter_pos = 2.0 * side_w + side_h + (half_ext - local.y);
        }

        float march_offset = time_ms * 0.001 * march_speed;
        float t = mod(perimeter_pos + march_offset, PATTERN_LEN);
        float dash = step(t, DASH_LEN);

        // Anti-alias edge fade
        float edge_dist = LINE_HALF_W;
        if (on_top)    edge_dist = dist_top;
        if (on_bottom) edge_dist = dist_bottom;
        if (on_left)   edge_dist = dist_left;
        if (on_right)  edge_dist = dist_right;
        float edge_fade = 1.0 - edge_dist / LINE_HALF_W;

        alpha = dash * edge_fade * zone_color.a;
    }

    // Interior sparkles
    vec2 d = abs(gp - center);
    float cheb = max(d.x, d.y);

    if (cheb < max_radius - 0.1) {
        vec2 rel = gp - center;
        bool skip_center = abs(rel.x) < 0.5 && abs(rel.y) < 0.5;

        if (!skip_center) {
            float time_s = time_ms * 0.001;
            float zone_span = max_radius * 2.0 - 1.0;

            for (int i = 0; i < SPARKLE_COUNT; i++) {
                float fi = float(i);
                vec2 seed = vec2(fi * 0.73, fi * 1.37);
                float sx = center.x + (hash(seed) - 0.5) * zone_span;
                float sy = center.y + (hash(seed + 1.0) - 0.5) * zone_span;

                // Skip sparkles outside zone
                float s_cheb = max(abs(sx - center.x), abs(sy - center.y));
                if (s_cheb >= max_radius - 0.1) continue;

                float phase = hash(seed + 2.0) * 6.2832;
                float speed = 0.5 + hash(seed + 3.0) * 2.0;
                float brightness = sin(time_s * speed + phase);
                if (brightness < SPARKLE_THRESHOLD) continue;
                brightness = (brightness - SPARKLE_THRESHOLD) / (1.0 - SPARKLE_THRESHOLD);

                vec2 sp = vec2(sx, sy) * cell_size;
                vec2 diff = px - sp;

                float size = 1.5 + brightness * 2.5;
                // Diamond: two perpendicular rhombuses = 4-point star
                float d1 = abs(diff.x) / size + abs(diff.y) / (size * 0.4);
                float d2 = abs(diff.x) / (size * 0.4) + abs(diff.y) / size;
                float star = min(d1, d2);

                if (star < 1.0) {
                    float sparkle_alpha = (1.0 - star) * brightness * 0.8;
                    // Alternate gold and blue
                    vec3 sparkle_col = mod(fi, 3.0) < 1.0
                        ? vec3(1.0, 0.94, 0.7)
                        : vec3(0.7, 0.86, 1.0);

                    col = mix(col, sparkle_col, step(alpha, sparkle_alpha * 0.8));
                    alpha = max(alpha, sparkle_alpha);
                }
            }
        }
    }

    if (alpha < 0.005) discard;
    COLOR = vec4(col, alpha);
}
