shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform float intensity : hint_range(0.0, 0.5) = 0.06;
uniform float speed : hint_range(0.0, 20.0) = 8.0;
uniform float luminance_response : hint_range(0.0, 1.0) = 0.6;

// Hash function for pseudo-random noise
float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

void fragment() {
    vec4 scene = texture(screen_texture, SCREEN_UV);

    // Animated grain - changes every frame
    float t = floor(TIME * speed);
    float grain = hash(SCREEN_UV * 1000.0 + t) * 2.0 - 1.0;

    // Grain is more visible in dark areas, less in bright areas
    float luminance = dot(scene.rgb, vec3(0.299, 0.587, 0.114));
    float mask = mix(1.0, 1.0 - luminance, luminance_response);

    vec3 color = scene.rgb + grain * intensity * mask;
    COLOR = vec4(color, 1.0);
}
