// Digital leaf/flower shader for MultiMeshInstance2D.
// COLOR channels: R=type(0=leaf,0.5=teardrop,1.0=flower), G=phase, B=size_factor.
// Leaf shapes: diamond, teardrop, 5-petal flower with center.
shader_type canvas_item;
render_mode blend_add;

uniform float progress     : hint_range(0.0, 1.0) = 1.0;
uniform float age_ms       : hint_range(0.0, 100000.0) = 0.0;
uniform vec4 leaf_color    : source_color = vec4(0.3, 0.9, 0.5, 1.0);
uniform vec4 accent_color  : source_color = vec4(1.0, 0.9, 0.7, 1.0);
uniform float fade_mult    : hint_range(0.01, 1.0) = 1.0;
uniform float base_alpha   : hint_range(0.0, 0.5) = 0.25;
uniform float sway_speed   : hint_range(0.1, 10.0) = 1.0;
uniform float sway_amount  : hint_range(0.0, 10.0) = 1.2;
uniform float sparkle      : hint_range(0.0, 1.0) = 0.2;
uniform bool grow_mode     = false;
uniform float trail        : hint_range(0.01, 0.5) = 0.2;

float easeOutCubic(float t) {
	return 1.0 - pow(1.0 - clamp(t, 0.0, 1.0), 3.0);
}

void vertex() {
	float phase = COLOR.g;
	float size_factor = 0.4 + COLOR.b * 0.6;
	float leaf_type = COLOR.r;

	float sway = sin(age_ms * sway_speed * 0.001 + phase * 6.283) * sway_amount;
	VERTEX.x += sway;
	VERTEX.y += cos(age_ms * sway_speed * 0.0007 + phase * 4.5 + 1.3) * sway_amount * 0.45;

	float breathe = 0.92 + 0.08 * sin(age_ms * 0.002 + phase * 3.14);
	VERTEX *= breathe * size_factor;
}

void fragment() {
	float leaf_type = COLOR.r;
	float phase = COLOR.g;

	float brightness;
	if (grow_mode) {
		float p = easeOutCubic(progress);
		float reveal = p * 1.3;
		float dist = 0.0;
		float grow_bright = reveal > dist ? 1.0 : max(0.0, 1.0 - (dist - reveal) * 4.0);
		float trail_p = p * (1.0 + trail);
		float diff = trail_p - dist;
		float trail_bright = diff < 0.0 ? 0.0 : clamp(1.0 - diff / trail, 0.0, 1.0);
		brightness = max(grow_bright, trail_bright);
	} else {
		brightness = 1.0;
	}

	brightness = max(brightness, base_alpha);
	if (brightness < 0.01) discard;

	float shimmer = 0.85 + 0.15 * sin(age_ms * 0.003 + phase * 6.283);

	vec2 centered = UV - 0.5;
	float shape = 0.0;

	if (leaf_type < 0.33) {
		// Diamond leaf
		float d = abs(centered.x) + abs(centered.y);
		shape = 1.0 - smoothstep(0.22, 0.42, d * 2.0);
		// Central vein
		float vein = 1.0 - smoothstep(0.0, 0.02, abs(centered.x) * 2.0);
		vein *= step(0.0, centered.y + 0.2) * step(centered.y, 0.25);
		brightness = mix(brightness, brightness * 1.4, vein * 0.5);
	} else if (leaf_type < 0.66) {
		// Teardrop leaf — pointed top, rounded bottom
		float r = length(centered);
		float angle = atan(centered.y, centered.x);
		float teardrop = 0.28 + 0.12 * cos(angle) + 0.06 * cos(angle * 2.0);
		shape = 1.0 - smoothstep(teardrop - 0.03, teardrop + 0.03, r);
		// Rotation via phase
		shape *= step(0.0, 1.0);
		// Central vein
		float vein = 1.0 - smoothstep(0.0, 0.015, abs(centered.x));
		vein *= step(-0.15, centered.y) * step(centered.y, 0.28);
		brightness = mix(brightness, brightness * 1.3, vein * 0.4);
	} else {
		// Flower: 5 petals with glowing center
		float r = length(centered);
		float angle = atan(centered.y, centered.x) + age_ms * 0.0003;
		float petal_r = 0.22 + 0.1 * pow(max(0.0, cos(angle * 5.0)), 1.5);
		shape = 1.0 - smoothstep(petal_r - 0.02, petal_r + 0.02, r);
		// Glowing center
		float center_glow = 1.0 - smoothstep(0.0, 0.07, r);
		shape = max(shape, center_glow * 0.9);
	}

	if (shape < 0.01) discard;

	float sparkle_phase = sin(age_ms * 0.005 + phase * 10.0);
	float sparkle_val = sparkle * max(0.0, sparkle_phase * sparkle_phase);

	vec3 col;
	if (leaf_type >= 0.66) {
		// Flower uses accent color for petals, bright center
		float r = length(centered);
		float center_mix = 1.0 - smoothstep(0.0, 0.07, r);
		col = mix(leaf_color.rgb, accent_color.rgb, 0.3 + 0.4 * center_mix);
		col += vec3(0.3) * center_mix;
	} else {
		col = leaf_color.rgb;
		col.r += 0.05 * sin(phase * 2.5);
		col.g += 0.04 * cos(phase * 2.8 + 0.3);
		col.b += 0.06 * sin(phase * 1.7 + 0.7);
	}

	float tip_glow = smoothstep(0.4, 1.0, brightness);
	col = mix(col, vec3(0.85, 1.0, 0.92), tip_glow * 0.35);
	col += vec3(sparkle_val);

	float alpha = brightness * fade_mult * shimmer * shape;
	if (alpha < 0.01) discard;

	COLOR = vec4(col, alpha);
}
