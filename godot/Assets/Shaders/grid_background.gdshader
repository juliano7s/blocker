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

void fragment() {
    // UV is 0..1 over the whole ColorRect (grid + padding*2)
    vec2 total_size = vec2(grid_size) * cell_size + grid_padding * 2.0;
    vec2 local_pos = UV * total_size;
    
    // Default color to prevent any transparency/white leaks
    vec4 out_color = vec4(0.0, 0.0, 0.0, 1.0);
    
    // Check if we are in padding area
    if (local_pos.x < grid_padding || local_pos.y < grid_padding || 
        local_pos.x > total_size.x - grid_padding || local_pos.y > total_size.y - grid_padding) {
        out_color = vec4(normal_color.rgb * 0.5, 1.0);
    } else {
        // Position within the grid (0,0 is top-left of first cell)
        vec2 grid_local = local_pos - grid_padding;
        ivec2 cell_coords = ivec2(floor(grid_local / cell_size));
        
        // Clamp to avoid edge artifacts
        cell_coords = clamp(cell_coords, ivec2(0), grid_size - 1);
        
        // Sample map data
        // texture coords: center of the pixel to avoid bleeding
        vec2 tex_uv = (vec2(cell_coords) + 0.5) / vec2(grid_size);
        float data = texture(map_data, tex_uv).r;
        int raw_val = int(round(data * 255.0));
        
        int ground_type = raw_val & 0x0F;
        
        // Select base ground color
        vec4 base_color = normal_color;
        if (ground_type == 1) base_color = boot_color;
        else if (ground_type == 2) base_color = overload_color;
        else if (ground_type == 3) base_color = proto_color;
        
        out_color = vec4(base_color.rgb, 1.0);
        
        // Grid lines
        vec2 grid_dist = abs(fract(grid_local / cell_size + 0.5) - 0.5) * cell_size;
        float line_factor = min(grid_dist.x, grid_dist.y);
        
        if (line_factor < grid_line_width * 0.5) {
            // Simple blend for grid lines
            float line_alpha = grid_line_color.a * (1.0 - smoothstep(grid_line_width * 0.4, grid_line_width * 0.5, line_factor));
            out_color.rgb = mix(out_color.rgb, grid_line_color.rgb, line_alpha);
        }
    }
    
    COLOR = out_color;
}
