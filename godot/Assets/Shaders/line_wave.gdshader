// Line2D wave animation shader.
// CPU sets Line2D points + gradient once at spawn. Gradient R channel encodes
// normalized distance along the line (0..1). GPU handles ALL animation.
// Per-frame CPU cost: 2 float uniform updates (progress, age_ms).
shader_type canvas_item;
render_mode blend_add;

// Animation
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms   : hint_range(0.0, 100000.0) = 0.0;
uniform float trail    : hint_range(0.01, 0.5) = 0.15;

// Visuals
uniform vec4 line_color : source_color = vec4(0.3, 0.8, 1.0, 1.0);
uniform float fade_mult : hint_range(0.01, 1.0) = 1.0;

// Flags
uniform bool reverse    = false;
uniform bool dashed     = false;
uniform bool flicker    = false;
uniform bool loop_mode  = false;
uniform bool contract   = false;   // uproot: outer segments fade first
uniform float fade_speed : hint_range(0.1, 2.0) = 0.7; // progress fade multiplier
uniform float base_alpha : hint_range(0.0, 0.5) = 0.0;

float easeOutCubic(float t) {
	return 1.0 - pow(1.0 - clamp(t, 0.0, 1.0), 3.0);
}

void fragment() {
	float pos = COLOR.r; // normalized distance along line, from Gradient

	float p = easeOutCubic(progress);
	float wave = p * (1.0 + trail);

	float brightness;
	if (contract) {
		// Uproot: smooth contracting fade — outer segments (pos=1) vanish first
		brightness = max(0.0, 1.0 - progress * (1.0 + pos));
	} else {
		float diff = reverse ? (wave - (1.0 - pos)) : (wave - pos);
		brightness = diff < 0.0 ? 0.0 : clamp(1.0 - diff / trail, 0.0, 1.0);
	}

	// Flicker: sinusoidal on/off per position (ghost flicker effect)
	if (flicker) {
		float phase = sin(age_ms * 0.01 + pos * 27.0);
		brightness *= phase > 0.2 ? abs(phase) * (1.0 - progress * 0.5) : 0.0;
	}

	// Dashed: animated crawl with taper
	if (dashed && brightness > 0.01) {
		float dp = fract(pos * 12.0 + age_ms * 0.00015);
		float taper = 1.0 - 0.3 * pos;
		brightness *= step(dp, 0.6) * taper;
	}

	// Base alpha: persistent glow for looping ZoC effects
	brightness = max(brightness, base_alpha);
	if (brightness < 0.01) discard;

	float shimmer = 0.8 + 0.2 * sin(age_ms * 0.006);
	float fade_out = loop_mode ? 1.0 : (1.0 - progress * fade_speed);
	float alpha = brightness * fade_mult * fade_out * shimmer;

	vec3 col = line_color.rgb;
	// White-hot tip on bright segments
	if (brightness > 0.7) {
		col = mix(col, vec3(1.0), (brightness - 0.7) / 0.3 * 0.5);
	}

	COLOR = vec4(col, alpha);
}
