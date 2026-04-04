shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float contrast : hint_range(0.5, 2.0) = 1.15;
uniform float saturation : hint_range(0.0, 2.0) = 1.2;
uniform float brightness : hint_range(-0.5, 0.5) = 0.0;
uniform float black_crush : hint_range(0.0, 0.1) = 0.02;
uniform vec3 shadow_tint : source_color = vec3(0.06, 0.04, 0.14);
uniform float shadow_tint_strength : hint_range(0.0, 1.0) = 0.3;

void fragment() {
    vec4 scene = texture(screen_texture, SCREEN_UV);
    vec3 color = scene.rgb;

    // Brightness
    color += brightness;

    // Contrast (around midpoint 0.5)
    color = (color - 0.5) * contrast + 0.5;

    // Black crush — push darks lower
    color = max(color - black_crush, vec3(0.0));

    // Saturation
    float luma = dot(color, vec3(0.299, 0.587, 0.114));
    color = mix(vec3(luma), color, saturation);

    // Shadow tint — blend tint into dark regions
    float shadow_mask = 1.0 - smoothstep(0.0, 0.25, luma);
    color = mix(color, shadow_tint, shadow_mask * shadow_tint_strength);

    COLOR = vec4(clamp(color, vec3(0.0), vec3(1.0)), 1.0);
}
