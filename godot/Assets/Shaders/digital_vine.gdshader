// Digital vine shader for Line2D — tighter, slower, continuous.
// Gradient R channel encodes normalized distance (0..1).
// Designed for dense decorative vines hugging block perimeters.
shader_type canvas_item;
render_mode blend_add;

uniform float progress    : hint_range(0.0, 1.0) = 1.0;
uniform float age_ms      : hint_range(0.0, 100000.0) = 0.0;
uniform vec4 vine_color   : source_color = vec4(0.15, 0.7, 0.4, 1.0);
uniform float fade_mult   : hint_range(0.01, 1.0) = 1.0;
uniform float base_alpha  : hint_range(0.0, 0.5) = 0.3;
uniform float taper       : hint_range(0.0, 1.0) = 0.4;
uniform float shimmer_speed : hint_range(0.1, 10.0) = 1.2;
uniform float color_shift : hint_range(0.0, 0.3) = 0.05;
uniform bool grow_mode    = false;
uniform float trail       : hint_range(0.01, 0.5) = 0.2;
uniform vec2 sway_dir     = vec2(0.0, 1.0);
uniform float sway_speed  : hint_range(0.01, 2.0) = 0.2;
uniform float sway_amount : hint_range(0.0, 5.0) = 1.0;
uniform float sway_phase  : hint_range(0.0, 6.283) = 0.0;

void vertex() {
	float t = COLOR.r;
	float wave = sin(age_ms * sway_speed * 0.001 + t * 4.0 + sway_phase);
	float amp = sway_amount * t * (1.0 - t * 0.4);
	VERTEX += sway_dir * wave * amp;
}

float easeOutCubic(float t) {
	return 1.0 - pow(1.0 - clamp(t, 0.0, 1.0), 3.0);
}

void fragment() {
	float pos = COLOR.r;

	float brightness;
	if (grow_mode) {
		float p = easeOutCubic(progress);
		float reveal = p * 1.2;
		brightness = reveal > pos ? 1.0 : max(0.0, 1.0 - (pos - reveal) * 5.0);
		float trail_p = p * (1.0 + trail);
		float diff = trail_p - pos;
		float trail_bright = diff < 0.0 ? 0.0 : clamp(1.0 - diff / trail, 0.0, 1.0);
		brightness = max(brightness, trail_bright);
	} else {
		brightness = 1.0;
	}

	brightness = max(brightness, base_alpha);
	if (brightness < 0.01) discard;

	float taper_mult = 1.0 - taper * pos * pos;
	brightness *= taper_mult;

	float shimmer = 0.88 + 0.12 * sin(age_ms * shimmer_speed * 0.001 + pos * 6.0);

	float sway_color = sin(age_ms * 0.0008 + pos * 3.5);
	vec3 col = vine_color.rgb;
	col.r += color_shift * sway_color;
	col.g += color_shift * 0.4 * sin(sway_color * 1.2 + 0.8);
	col.b += color_shift * 0.25 * cos(sway_color * 0.7 + 1.8);

	float tip_glow = smoothstep(0.5, 1.0, brightness);
	col = mix(col, vec3(0.75, 1.0, 0.88), tip_glow * 0.35);

	float alpha = brightness * fade_mult * shimmer;

	COLOR = vec4(col, alpha);
}
