// Grid-aligned ring / beam shader for ColorRect covering the grid area.
// CPU sets center + timing uniforms. GPU computes per-pixel lighting
// based on Chebyshev distance or cardinal axis proximity.
// Per-frame CPU cost: 2 float uniform updates.
shader_type canvas_item;
render_mode blend_add;

uniform vec2 center     = vec2(15.5, 15.5);   // center in grid coords
uniform vec2 grid_size  = vec2(30.0, 30.0);    // number of cells
uniform float cell_size = 28.0;
uniform float progress  : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms    : hint_range(0.0, 100000.0) = 0.0;
uniform float max_radius : hint_range(1.0, 20.0) = 10.0;
uniform float trail     : hint_range(0.5, 10.0) = 2.0; // in grid cells
uniform vec4 ring_color : source_color = vec4(0.9, 0.9, 1.0, 1.0);
uniform float fade_mult : hint_range(0.01, 1.0) = 1.0;
uniform int mode        = 0;       // 0=square rings, 1=cardinal beams, 2=sine rings
uniform bool loop_mode  = false;
uniform float base_alpha : hint_range(0.0, 0.3) = 0.0;
uniform float line_w    : hint_range(0.02, 0.2) = 0.06; // grid line thickness (fraction of cell)

float easeOutCubic(float t) {
	return 1.0 - pow(1.0 - clamp(t, 0.0, 1.0), 3.0);
}

void fragment() {
	vec2 gp = UV * grid_size;      // position in grid coordinates
	vec2 frac = fract(gp);          // fractional position within cell

	// Grid line detection — smoothstep for anti-aliased lines
	float h_line = 1.0 - smoothstep(0.0, line_w, min(frac.y, 1.0 - frac.y));
	float v_line = 1.0 - smoothstep(0.0, line_w, min(frac.x, 1.0 - frac.x));
	float on_grid = max(h_line, v_line);

	// Chebyshev distance from center
	vec2 d = abs(gp - center);
	float cheb = max(d.x, d.y);

	// Soft fade at max_radius boundary — prevents hard square edge
	float edge_fade = 1.0 - smoothstep(max_radius - trail, max_radius, cheb);

	float p = easeOutCubic(progress);
	float wave = p * (max_radius + trail);
	float diff = wave - cheb;
	float brightness = diff > 0.0 ? clamp(1.0 - diff / trail, 0.0, 1.0) : 0.0;
	// Apply edge fade so the wave dissolves before hitting the hard boundary
	brightness *= edge_fade;

	float alpha = 0.0;

	if (mode == 0) {
		// Expanding square rings — light only on grid lines
		alpha = brightness * on_grid;
	}
	else if (mode == 1) {
		// Cardinal beams — gaussian profile along each axis
		float beam_width = 1.5 * (0.6 + 0.4 * sin(age_ms * 0.008));
		float beam_x = exp(-3.0 * pow(d.y / beam_width, 2.0));
		float beam_y = exp(-3.0 * pow(d.x / beam_width, 2.0));
		float beam = max(beam_x, beam_y);
		float beam_dist = beam_x > beam_y ? d.x : d.y;

		float beam_edge = 1.0 - smoothstep(max_radius - trail, max_radius, beam_dist);
		float bdiff = wave - beam_dist;
		float bbright = bdiff > 0.0 ? clamp(1.0 - bdiff / trail, 0.0, 1.0) : 0.0;
		alpha = bbright * beam_edge * beam;
	}
	else if (mode == 2) {
		// Sine-displaced rings — wobble the grid position before computing distance
		float fade_mult_local = loop_mode ? 1.0 : (1.0 - progress * 0.6);
		vec2 disp = vec2(
			sin(age_ms * 0.005 + gp.x * 0.8) * 0.3,
			sin(age_ms * 0.005 + gp.y * 0.8) * 0.3
		) * fade_mult_local;
		vec2 adj = gp + disp;
		float adj_cheb = max(abs(adj.x - center.x), abs(adj.y - center.y));

		float adj_edge = 1.0 - smoothstep(max_radius - trail, max_radius, adj_cheb);
		float adj_diff = wave - adj_cheb;
		float adj_bright = adj_diff > 0.0 ? clamp(1.0 - adj_diff / trail, 0.0, 1.0) : 0.0;
		alpha = adj_bright * adj_edge * on_grid;
	}

	// Base alpha: persistent zone glow only within max_radius
	if (base_alpha > 0.0) {
		float in_range = 1.0 - smoothstep(max_radius - 1.0, max_radius, cheb);
		alpha = max(alpha, base_alpha * on_grid * in_range);
	}
	alpha *= fade_mult;

	if (alpha < 0.005) discard;

	float shimmer = 0.8 + 0.2 * sin(age_ms * 0.006);
	float fade_out = loop_mode ? 1.0 : (1.0 - progress * 0.7);
	alpha *= shimmer * fade_out;

	vec3 col = ring_color.rgb;
	// White-hot tip for bright segments
	float bEst = brightness;
	if (bEst > 0.7) {
		col = mix(col, vec3(1.0), (bEst - 0.7) / 0.3 * 0.5);
	}

	COLOR = vec4(col, alpha);
}
