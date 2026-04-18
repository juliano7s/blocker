shader_type canvas_item;

uniform sampler2D map_data : filter_nearest, repeat_disable; // R8: Ground (lower 4 bits) | Terrain (upper 4 bits)
uniform ivec2 grid_size;
uniform float cell_size = 28.0;
uniform float grid_padding = 140.0;
uniform float grid_line_width = 1.0;

uniform vec4 normal_color : source_color;
uniform vec4 boot_color : source_color;
uniform vec4 overload_color : source_color;
uniform vec4 proto_color : source_color;
uniform vec4 grid_line_color : source_color;

// TS prototype sprite accents (hardcoded to match Sprites.ts buildGroundSprites)
const vec3 BOOT_STROKE    = vec3(0.227, 0.353, 0.227); // #3a5a3a
const vec3 BOOT_DIAMOND   = vec3(0.353, 0.541, 0.353); // #5a8a5a
const vec3 BOOT_DOT       = vec3(0.416, 0.667, 0.416); // #6aaa6a
const vec3 BOOT_PULSE     = vec3(0.314, 0.706, 0.314); // #50b450 — slow breathing fill
const vec3 OVERLOAD_RUNE  = vec3(0.416, 0.165, 0.686); // #6a2aaf (overloadGroundPulse)

float dash_on(float t, float on_len, float off_len) {
    float period = on_len + off_len;
    return step(mod(t, period), on_len - 0.0001);
}

// Boot: static accents (faint — main "life" comes from animated breathing overlay)
vec3 paint_boot(vec3 base, vec2 p, float cs, float pad, ivec2 cell) {
    vec3 col = base;

    // 1) Faint inset stroke
    float edge = min(min(p.x - pad, p.y - pad), min(cs - pad - p.x, cs - pad - p.y));
    if (edge >= 0.0 && edge <= 1.0) {
        col = mix(col, BOOT_STROKE, 0.22);
    }

    // 2) Central diamond, alpha 0.16
    float cx = cs * 0.5;
    float d = cs * 0.12;
    if (abs(p.x - cx) + abs(p.y - cx) < d) {
        col = mix(col, BOOT_DIAMOND, 0.16);
    }

    // 3) Four corner dots, alpha 0.12
    float inset_c = cs * 0.22;
    float r = cs * 0.04;
    vec2 centers[4] = vec2[4](
        vec2(inset_c, inset_c),
        vec2(cs - inset_c, inset_c),
        vec2(inset_c, cs - inset_c),
        vec2(cs - inset_c, cs - inset_c)
    );
    for (int i = 0; i < 4; i++) {
        if (distance(p, centers[i]) < r) {
            col = mix(col, BOOT_DOT, 0.12);
        }
    }

    // 4) Slow breathing fill — very faint, easy to miss by design
    float alpha_breath = 0.015 + 0.015 * sin(TIME * 1.5);
    col = mix(col, BOOT_PULSE, clamp(alpha_breath, 0.0, 1.0));

    return col;
}

// Overload: static rune cross + per-cell pulsing shimmer + animated rune brightness
vec3 paint_overload(vec3 base, vec2 p, float cs, ivec2 cell) {
    vec3 col = base;
    float cx = cs * 0.5;
    float lo = cs * 0.15;
    float hi = cs * 0.85;

    // Per-cell pulse phase — different for each cell so the field shimmers
    float phase = TIME * 3.0 + float(cell.x) * 0.7 + float(cell.y) * 1.1;
    float pulse = 0.5 + 0.5 * sin(phase);

    // Shimmer fill — very subtle, cell interior only
    if (p.x >= 2.0 && p.y >= 2.0 && p.x <= cs - 2.0 && p.y <= cs - 2.0) {
        float shimmer_a = 0.18 + 0.22 * pulse;
        col = mix(col, OVERLOAD_RUNE, shimmer_a);
    }

    // Rune cross — animated brightness, toned down
    float rune_a = 0.32 + 0.18 * pulse;
    if (abs(p.x - cx) < 0.5 && p.y >= lo && p.y <= hi) {
        col = mix(col, OVERLOAD_RUNE, rune_a);
    }
    if (abs(p.y - cx) < 0.5 && p.x >= lo && p.x <= hi) {
        col = mix(col, OVERLOAD_RUNE, rune_a);
    }
    return col;
}

// Proto: dashed inset border + corner L-brackets + mid-dashes + per-cell flicker
vec3 paint_proto(vec3 base, vec2 p, float cs, float pad, ivec2 cell) {
    vec3 col = base;
    vec3 accent = boot_color.rgb;

    float inner_x0 = pad;
    float inner_x1 = cs - pad;
    float inner_y0 = pad;
    float inner_y1 = cs - pad;

    float dx_in = min(p.x - inner_x0, inner_x1 - p.x);
    float dy_in = min(p.y - inner_y0, inner_y1 - p.y);

    // Per-cell subtle flicker — only on the dashed border region, not a full fill
    float flicker = 0.02 + 0.02 * sin(TIME * 1.5 + float(cell.x) * 0.7 + float(cell.y) * 1.1);

    bool on_border = false;
    float t_dash = 0.0;
    if (dx_in >= 0.0 && dy_in >= 0.0) {
        if (dy_in <= 0.75) {
            on_border = true;
            t_dash = p.x - inner_x0;
        } else if (dx_in <= 0.75) {
            on_border = true;
            t_dash = p.y - inner_y0;
        }
    }

    // Dashed inset rect [3,5], subtle
    if (on_border && dash_on(t_dash, 3.0, 5.0) > 0.5) {
        col = mix(col, accent, 0.35 + flicker);
    }

    // Corner L-brackets, length cs*0.25, alpha 0.55
    float cl = cs * 0.25;
    if (on_border) {
        bool in_L = false;
        if ((dy_in <= 0.75 && p.x >= inner_x0 && p.x <= inner_x0 + cl) ||
            (dx_in <= 0.75 && p.y >= inner_y0 && p.y <= inner_y0 + cl)) in_L = true;
        if ((dy_in <= 0.75 && p.x >= inner_x1 - cl && p.x <= inner_x1) ||
            (dx_in <= 0.75 && p.y >= inner_y0 && p.y <= inner_y0 + cl)) in_L = true;
        if ((dy_in <= 0.75 && p.x >= inner_x0 && p.x <= inner_x0 + cl) ||
            (dx_in <= 0.75 && p.y >= inner_y1 - cl && p.y <= inner_y1)) in_L = true;
        if ((dy_in <= 0.75 && p.x >= inner_x1 - cl && p.x <= inner_x1) ||
            (dx_in <= 0.75 && p.y >= inner_y1 - cl && p.y <= inner_y1)) in_L = true;
        if (in_L) {
            col = mix(col, accent, 0.4 + flicker);
        }
    }

    // Mid-lines dashed [2,6], very subtle
    float cx = cs * 0.5;
    if (abs(p.x - cx) < 0.5 && p.y >= pad + 2.0 && p.y <= cs - pad - 2.0) {
        if (dash_on(p.y - (pad + 2.0), 2.0, 6.0) > 0.5) {
            col = mix(col, accent, 0.18);
        }
    }
    if (abs(p.y - cx) < 0.5 && p.x >= pad + 2.0 && p.x <= cs - pad - 2.0) {
        if (dash_on(p.x - (pad + 2.0), 2.0, 6.0) > 0.5) {
            col = mix(col, accent, 0.18);
        }
    }

    return col;
}

void fragment() {
    vec2 total_size = vec2(grid_size) * cell_size + grid_padding * 2.0;
    vec2 local_pos = UV * total_size;

    vec4 out_color = vec4(0.0, 0.0, 0.0, 1.0);

    if (local_pos.x < grid_padding || local_pos.y < grid_padding ||
        local_pos.x > total_size.x - grid_padding || local_pos.y > total_size.y - grid_padding) {
        out_color = vec4(normal_color.rgb * 0.5, 1.0);
    } else {
        vec2 grid_local = local_pos - grid_padding;
        ivec2 cell_coords = ivec2(floor(grid_local / cell_size));
        cell_coords = clamp(cell_coords, ivec2(0), grid_size - 1);

        vec2 tex_uv = (vec2(cell_coords) + 0.5) / vec2(grid_size);
        float data = texture(map_data, tex_uv).r;
        int raw_val = int(round(data * 255.0));
        int ground_type = raw_val & 0x0F;

        vec4 base_color = normal_color;
        if (ground_type == 1) base_color = boot_color;
        else if (ground_type == 2) base_color = overload_color;
        else if (ground_type == 3) base_color = proto_color;

        vec3 rgb = base_color.rgb;
        vec2 p_cell = grid_local - vec2(cell_coords) * cell_size;
        float pad = 2.0;

        if (ground_type == 1) rgb = paint_boot(rgb, p_cell, cell_size, pad, cell_coords);
        else if (ground_type == 2) rgb = paint_overload(rgb, p_cell, cell_size, cell_coords);
        else if (ground_type == 3) rgb = paint_proto(rgb, p_cell, cell_size, pad, cell_coords);

        out_color = vec4(rgb, 1.0);

        // Grid lines
        vec2 grid_dist = abs(fract(grid_local / cell_size + 0.5) - 0.5) * cell_size;
        float line_factor = min(grid_dist.x, grid_dist.y);

        if (line_factor < grid_line_width * 0.5) {
            float line_alpha = grid_line_color.a * (1.0 - smoothstep(grid_line_width * 0.4, grid_line_width * 0.5, line_factor));
            out_color.rgb = mix(out_color.rgb, grid_line_color.rgb, line_alpha);
        }
    }

    COLOR = out_color;
}
