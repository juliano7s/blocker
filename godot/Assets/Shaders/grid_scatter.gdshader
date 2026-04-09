// Grid-aligned scatter shader for ColorRect.
// GPU computes pseudo-random per-cell patterns using hash functions.
// Modes: 0 = ghost flicker, 1 = digital cascade.
// Per-frame CPU cost: 2 float uniform updates.
shader_type canvas_item;
render_mode blend_add;

uniform vec2 center       = vec2(15.5, 15.5);
uniform vec2 grid_size    = vec2(30.0, 30.0);
uniform float progress    : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms      : hint_range(0.0, 100000.0) = 0.0;
uniform float radius      : hint_range(1.0, 15.0) = 6.0;
uniform vec4 effect_color : source_color = vec4(0.75, 0.6, 1.0, 1.0);
uniform float fade_mult   : hint_range(0.01, 1.0) = 1.0;
uniform int mode          = 0;  // 0=flicker, 1=cascade

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float easeOutCubic(float t) {
	return 1.0 - pow(1.0 - clamp(t, 0.0, 1.0), 3.0);
}

void fragment() {
	vec2 gp = UV * grid_size;
	vec2 cell = floor(gp);
	vec2 frac = fract(gp);
	// Cell interior fill (not just grid lines — these effects fill cells)
	float interior = 1.0 - smoothstep(0.05, 0.15, min(min(frac.x, 1.0 - frac.x), min(frac.y, 1.0 - frac.y)));

	float alpha = 0.0;
	float brightness = 0.0;

	if (mode == 0) {
		// Ghost flicker: random cells in radius flicker on/off
		vec2 cell_ctr = cell + 0.5;
		float dist = max(abs(cell_ctr.x - center.x), abs(cell_ctr.y - center.y));
		if (dist <= radius + 0.5) {
			float r = hash(cell);
			if (r < 0.35) {
				float phase = sin(age_ms * 0.01 + r * 27.0);
				brightness = phase > 0.2 ? abs(phase) * (1.0 - progress * 0.5) : 0.0;
				brightness *= 1.0 - dist / (radius + 1.0);
				alpha = brightness * interior;
			}
		}
	}
	else if (mode == 1) {
		// Digital cascade: vertical rain columns near center
		float col_dist = abs(cell.x + 0.5 - center.x);
		if (col_dist < 5.0) {
			float col_seed = hash(vec2(cell.x, 0.0));
			float start_y = center.y - 2.0 + col_seed * 4.0;
			float col_len = 6.0 + col_seed * 8.0;
			float stagger = hash(vec2(cell.x, 1.0)) * 10.0;

			float row = cell.y - start_y;
			if (row >= 0.0 && row < col_len) {
				float max_d = col_len + stagger + 5.0;
				float wave = easeOutCubic(progress) * max_d;
				float diff = wave - row;
				brightness = diff > 0.0 ? clamp(1.0 - diff / 5.0, 0.0, 1.0) : 0.0;
				alpha = brightness * 0.7 * interior;

				// Horizontal branches at some intersections
				float bh = hash(cell * 1.7);
				if (bh < 0.3 && row > 1.0) {
					alpha = min(alpha * 1.5, 1.0);
				}
			}
		}
	}

	alpha *= fade_mult;
	float shimmer = 0.8 + 0.2 * sin(age_ms * 0.006);
	float fade_out = 1.0 - progress * 0.7;
	alpha *= shimmer * fade_out;

	if (alpha < 0.01) discard;

	vec3 col = effect_color.rgb;
	if (brightness > 0.7) {
		col = mix(col, vec3(1.0), (brightness - 0.7) / 0.3 * 0.5);
	}

	COLOR = vec4(col, alpha);
}
