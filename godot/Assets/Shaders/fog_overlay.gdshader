shader_type canvas_item;

// fog_data: R8 texture, one pixel per grid cell.
//   0.0 = shroud (unexplored, never seen) -> full black
//   ~0.5 = fog of war (explored, not currently visible) -> dark overlay
//   1.0 = visible (in LoS this tick) -> transparent
uniform sampler2D fog_data : filter_linear, repeat_disable;
uniform ivec2 grid_size;
uniform float cell_size = 28.0;
uniform float grid_padding = 140.0;

void fragment() {
    vec2 total_size = vec2(grid_size) * cell_size + grid_padding * 2.0;
    vec2 local_pos = UV * total_size;

    // Outside grid area — fully dark (padding and beyond)
    if (local_pos.x < grid_padding || local_pos.y < grid_padding ||
        local_pos.x > total_size.x - grid_padding ||
        local_pos.y > total_size.y - grid_padding) {
        COLOR = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec2 grid_local = local_pos - grid_padding;
    vec2 cell_frac = grid_local / cell_size;
    vec2 tex_uv = cell_frac / vec2(grid_size);
    vec2 texel = 1.0 / vec2(grid_size);

    // 5-tap weighted sample: hardware bilinear + 4 neighbours at 0.6 texel offset
    // Widens the transition zone to ~2 cells for a soft edge
    float c = texture(fog_data, tex_uv).r;
    float r = texture(fog_data, tex_uv + vec2(texel.x * 0.6, 0.0)).r;
    float l = texture(fog_data, tex_uv - vec2(texel.x * 0.6, 0.0)).r;
    float d = texture(fog_data, tex_uv + vec2(0.0, texel.y * 0.6)).r;
    float u = texture(fog_data, tex_uv - vec2(0.0, texel.y * 0.6)).r;
    float fog_val = c * 0.4 + (r + l + d + u) * 0.15;

    // Map fog_val to overlay alpha:
    //   0.0 (shroud)  -> alpha 1.0, colour pure black
    //   0.5 (fog)     -> alpha 0.65, colour near-black with blue hint
    //   1.0 (visible) -> alpha 0.0, fully transparent
    float alpha;
    vec3 col;

    if (fog_val < 0.5) {
        float t = fog_val * 2.0;
        alpha = mix(1.0, 0.65, smoothstep(0.0, 1.0, t));
        col = vec3(0.0, 0.0, 0.01);
    } else {
        float t = (fog_val - 0.5) * 2.0;
        alpha = mix(0.65, 0.0, smoothstep(0.0, 1.0, t));
        col = vec3(0.0, 0.01, 0.04);
    }

    // Subtle per-pixel noise on the fog boundary for a hazy appearance
    float noise = fract(sin(dot(tex_uv * 300.0, vec2(12.9898, 78.233))) * 43758.5453);
    alpha = clamp(alpha + (noise - 0.5) * 0.06, 0.0, 1.0);

    COLOR = vec4(col, alpha);
}