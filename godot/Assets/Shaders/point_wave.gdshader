// MultiMesh point/dot shader.
// Each instance encodes its normalized distance in COLOR.r and a phase
// variation in COLOR.g. GPU animates the traveling wave per-instance.
// Per-frame CPU cost: 2 float uniform updates for thousands of dots.
shader_type canvas_item;
render_mode blend_add;

uniform float progress    : hint_range(0.0, 1.0) = 0.0;
uniform float age_ms      : hint_range(0.0, 100000.0) = 0.0;
uniform float trail       : hint_range(0.01, 0.5) = 0.15;
uniform vec4 dot_color    : source_color = vec4(0.9, 0.7, 1.0, 1.0);
uniform float fade_mult   : hint_range(0.01, 1.0) = 1.0;
uniform bool loop_mode    = false;
uniform float base_alpha  : hint_range(0.0, 0.5) = 0.0;

float easeOutCubic(float t) {
	return 1.0 - pow(1.0 - clamp(t, 0.0, 1.0), 3.0);
}

void fragment() {
	float dist = COLOR.r;   // normalized distance (0..1)
	float phase = COLOR.g;  // per-instance phase variation

	float p = easeOutCubic(progress);
	float wave = p * (1.0 + trail);
	float diff = wave - dist;
	float brightness = diff < 0.0 ? 0.0 : clamp(1.0 - diff / trail, 0.0, 1.0);

	brightness = max(brightness, base_alpha);
	if (brightness < 0.01) discard;

	float shimmer = 0.8 + 0.2 * sin(age_ms * 0.006 + phase * 6.283);
	float fade_out = loop_mode ? 1.0 : (1.0 - progress * 0.7);
	float alpha = brightness * fade_mult * fade_out * shimmer;

	vec3 col = dot_color.rgb;
	if (brightness > 0.7) {
		col = mix(col, vec3(1.0), (brightness - 0.7) / 0.3 * 0.5);
	}

	// Soft circular dot shape
	vec2 centered = UV - 0.5;
	float d = length(centered) * 2.0;
	float shape = 1.0 - smoothstep(0.35, 0.9, d);
	alpha *= shape;

	if (alpha < 0.01) discard;

	COLOR = vec4(col, alpha);
}
